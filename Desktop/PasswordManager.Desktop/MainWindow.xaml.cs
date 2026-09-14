using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using PasswordManager.Desktop.Models;
using PasswordManager.Desktop.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Color = System.Windows.Media.Color;

using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;

namespace PasswordManager.Desktop;

public partial class MainWindow : FluentWindow
{
    private readonly CryptoService _cryptoService = new();
    private DatabaseService? _dbService;
    private byte[]? _derivedMasterKey;
    private string? _lastCopiedPassword;
    private CancellationTokenSource? _clipboardCts;
    private LocalServerService? _localServer;

    private NotifyIcon? _notifyIcon;
    private AppSettings _settings = new();
    private DispatcherTimer? _autoLockTimer;
    private DateTime _lastActivityTime = DateTime.UtcNow;
    private bool _isExplicitExit;

    private readonly string _saltFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vault.salt");
    private readonly string _dbFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vault.db");

    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(16, 124, 65));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(209, 52, 56));

    public bool IsVaultUnlocked => _derivedMasterKey != null;
    public bool HasVaultInitialized => File.Exists(_dbFilePath) && File.Exists(_saltFilePath);

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsService.Load();

        Loaded += MainWindow_Loaded;
        InitializeTrayIcon();
        InitializeAutoLockTimer();

        // Start secure loopback server for the browser extension
        _localServer = new LocalServerService(this);
        _localServer.Start();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyConfiguredTheme();

        MinimizeToTraySwitch.IsChecked = _settings.MinimizeToTray;
        StartAtLogonSwitch.IsChecked = IsStartupEnabled();
        AutoLockSwitch.IsChecked = _settings.AutoLockEnabled;
        AutoLockHoursBox.Text = _settings.AutoLockHours.ToString();
        AutoLockHoursBox.IsEnabled = _settings.AutoLockEnabled;
        ClipboardNotificationSwitch.IsChecked = _settings.ShowClipboardNotification;

        ConfigureViewState();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExplicitExit && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
        {
            Hide();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _localServer?.Stop();
        _localServer = null;

        _notifyIcon?.Dispose();
        _notifyIcon = null;

        WipeSessionMemory();
        base.OnClosed(e);
    }

    // ================= Extension Bridge Handlers =================

    public async Task<bool> UnlockFromIpcAsync(string password)
    {
        if (!HasVaultInitialized || string.IsNullOrWhiteSpace(password)) return false;

        try
        {
            byte[] salt = await File.ReadAllBytesAsync(_saltFilePath);
            byte[] derivedKey = await _cryptoService.DeriveKeyAsync(password, salt);

            var testDb = new DatabaseService(_dbFilePath, password);
            var items = await testDb.GetAllAsync();

            if (items.Count > 0)
            {
                var testItem = items[0];
                _cryptoService.Decrypt(testItem.EncryptedPassword, testItem.Nonce, testItem.AuthTag, derivedKey);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                SetMasterKey(derivedKey);
                _dbService = testDb;
                VaultItemsContainer.ItemsSource = items;
                EmptyVaultText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                TransitionToVaultView();
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task LockFromIpcAsync()
    {
        await Dispatcher.InvokeAsync(() =>
        {
            OnLockClicked(this, new RoutedEventArgs());
        });
    }

    public async Task<List<object>> GetLoginsForIpcAsync()
    {
        if (_dbService == null) return new List<object>();
        var items = await _dbService.GetAllAsync();
        return items.Select(i => (object)new
        {
            id = i.Id,
            title = i.Title,
            username = i.Username,
            websiteUrl = i.WebsiteUrl
        }).ToList();
    }

    public async Task<object?> GetDecryptedCredentialForIpcAsync(string id)
    {
        if (_dbService == null || _derivedMasterKey == null) return null;
        var items = await _dbService.GetAllAsync();
        var item = items.FirstOrDefault(i => i.Id == id);
        if (item == null) return null;

        string decrypted = _cryptoService.Decrypt(item.EncryptedPassword, item.Nonce, item.AuthTag, _derivedMasterKey);
        return new
        {
            username = item.Username,
            password = decrypted
        };
    }

    // ================= Desktop UI & Security =================

    private void InitializeTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "FennecGuard",
            Visible = true
        };

        try
        {
            var streamInfo = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/logo.ico"));
            if (streamInfo != null)
            {
                using var stream = streamInfo.Stream;
                _notifyIcon.Icon = new System.Drawing.Icon(stream);
            }
            else
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "logo.ico");
                _notifyIcon.Icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Shield;
            }
        }
        catch
        {
            _notifyIcon.Icon = System.Drawing.SystemIcons.Shield;
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open FennecGuard", null, (s, e) => Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add("Lock Vault", null, (s, e) => Dispatcher.Invoke(LockFromTray));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Dispatcher.Invoke(ExitFromTray));

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (s, e) => Dispatcher.Invoke(RestoreFromTray);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void LockFromTray()
    {
        if (_derivedMasterKey != null)
        {
            OnLockClicked(this, new RoutedEventArgs());
        }
        RestoreFromTray();
    }

    private void ExitFromTray()
    {
        _isExplicitExit = true;
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        WipeSessionMemory();
        Application.Current.Shutdown();
    }

    private void InitializeAutoLockTimer()
    {
        _autoLockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autoLockTimer.Tick += OnAutoLockTimerTick;
        _autoLockTimer.Start();
    }

    private void OnAutoLockTimerTick(object? sender, EventArgs e)
    {
        if (_settings.AutoLockEnabled && _derivedMasterKey != null)
        {
            var elapsed = DateTime.UtcNow - _lastActivityTime;
            if (elapsed.TotalHours >= _settings.AutoLockHours)
            {
                WipeSessionMemory();
                _dbService = null;
                VaultItemsContainer.ItemsSource = null;
                ConfigureViewState();
            }
        }
    }

    private void UpdateActivity()
    {
        _lastActivityTime = DateTime.UtcNow;
    }

    private void ConfigureViewState()
    {
        bool isFirstRun = !File.Exists(_dbFilePath) || !File.Exists(_saltFilePath);
        var workArea = SystemParameters.WorkArea;

        if (isFirstRun)
        {
            Width = 520;
            Height = 520;
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Top + (workArea.Height - Height) / 2;

            SetupPanel.Visibility = Visibility.Visible;
            UnlockPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            Width = 340;
            Height = 280;
            Left = workArea.Right - Width - 24;
            Top = workArea.Bottom - Height - 24;

            SetupPanel.Visibility = Visibility.Collapsed;
            UnlockPanel.Visibility = Visibility.Visible;
            UnlockPasswordBox.Focus();
        }

        VaultPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        CopyNotificationOverlay.Visibility = Visibility.Collapsed;

        SetupMasterPasswordBox.Clear();
        SetupConfirmPasswordBox.Clear();
        UnlockPasswordBox.Clear();
        UnlockStatusText.Text = string.Empty;
        SetupStatusText.Text = string.Empty;
    }

    private void TransitionToVaultView()
    {
        var workArea = SystemParameters.WorkArea;
        Width = 1020;
        Height = 740;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;

        UnlockPanel.Visibility = Visibility.Collapsed;
        SetupPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        CopyNotificationOverlay.Visibility = Visibility.Collapsed;
        VaultPanel.Visibility = Visibility.Visible;
        UpdateActivity();
    }

    private async void OnInitializeVaultClicked(object sender, RoutedEventArgs e)
    {
        string password = SetupMasterPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            SetupStatusText.Foreground = ErrorBrush;
            SetupStatusText.Text = "Password must be at least 8 characters.";
            return;
        }

        if (password != SetupConfirmPasswordBox.Password)
        {
            SetupStatusText.Foreground = ErrorBrush;
            SetupStatusText.Text = "Passwords do not match.";
            return;
        }

        try
        {
            byte[] salt = _cryptoService.GenerateSalt();
            await File.WriteAllBytesAsync(_saltFilePath, salt);

            byte[] key = await _cryptoService.DeriveKeyAsync(password, salt);
            SetMasterKey(key);

            _dbService = new DatabaseService(_dbFilePath, password);
            await _dbService.InitializeDatabaseAsync();
            await RefreshVaultListAsync();

            TransitionToVaultView();
            SetupMasterPasswordBox.Clear();
            SetupConfirmPasswordBox.Clear();
        }
        catch (Exception ex)
        {
            SetupStatusText.Foreground = ErrorBrush;
            SetupStatusText.Text = $"Error initializing vault: {ex.Message}";
        }
    }

    private void OnUnlockKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            OnUnlockClicked(sender, e);
        }
    }

    private async void OnUnlockClicked(object sender, RoutedEventArgs e)
    {
        string password = UnlockPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(password))
        {
            UnlockStatusText.Foreground = ErrorBrush;
            UnlockStatusText.Text = "Enter master password.";
            return;
        }

        UnlockButton.IsEnabled = false;
        UnlockStatusText.Foreground = SuccessBrush;
        UnlockStatusText.Text = "Unlocking...";

        try
        {
            byte[] salt = await File.ReadAllBytesAsync(_saltFilePath);
            byte[] derivedKey = await _cryptoService.DeriveKeyAsync(password, salt);

            var testDb = new DatabaseService(_dbFilePath, password);
            var items = await testDb.GetAllAsync();

            if (items.Count > 0)
            {
                var testItem = items[0];
                _cryptoService.Decrypt(testItem.EncryptedPassword, testItem.Nonce, testItem.AuthTag, derivedKey);
            }

            SetMasterKey(derivedKey);
            _dbService = testDb;

            VaultItemsContainer.ItemsSource = items;
            EmptyVaultText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            TransitionToVaultView();
            UnlockPasswordBox.Clear();
            UnlockStatusText.Text = string.Empty;
        }
        catch
        {
            UnlockStatusText.Foreground = ErrorBrush;
            UnlockStatusText.Text = "Incorrect password.";
        }
        finally
        {
            UnlockButton.IsEnabled = true;
        }
    }

    private async Task RefreshVaultListAsync()
    {
        if (_dbService == null) return;
        var items = await _dbService.GetAllAsync();
        VaultItemsContainer.ItemsSource = items;
        EmptyVaultText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnAddCredentialClicked(object sender, RoutedEventArgs e)
    {
        if (_dbService == null || _derivedMasterKey == null) return;
        UpdateActivity();

        string title = NewTitleBox.Text.Trim();
        string username = NewUsernameBox.Text.Trim();
        string url = NewUrlBox.Text.Trim();
        string plainPassword = NewPasswordBox.Password;

        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(plainPassword)) return;

        var (ciphertext, nonce, authTag) = _cryptoService.Encrypt(plainPassword, _derivedMasterKey);

        var newItem = new VaultItem
        {
            Title = title,
            Username = username,
            WebsiteUrl = url,
            EncryptedPassword = ciphertext,
            Nonce = nonce,
            AuthTag = authTag
        };

        await _dbService.AddItemAsync(newItem);

        NewTitleBox.Clear();
        NewUsernameBox.Clear();
        NewUrlBox.Clear();
        NewPasswordBox.Clear();

        await RefreshVaultListAsync();
    }

    private void OnCopyPasswordClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is VaultItem item && _derivedMasterKey != null)
        {
            UpdateActivity();
            try
            {
                string decrypted = _cryptoService.Decrypt(item.EncryptedPassword, item.Nonce, item.AuthTag, _derivedMasterKey);
                Clipboard.SetText(decrypted);
                _lastCopiedPassword = decrypted;

                _clipboardCts?.Cancel();
                _clipboardCts = new CancellationTokenSource();
                var token = _clipboardCts.Token;

                Task.Run(async () =>
                {
                    await Task.Delay(30000, token);
                    if (!token.IsCancellationRequested)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (Clipboard.GetText() == _lastCopiedPassword)
                            {
                                Clipboard.Clear();
                                _lastCopiedPassword = null;
                            }
                        });
                    }
                }, token);

                if (_settings.ShowClipboardNotification)
                {
                    DontShowCopyNotificationCheck.IsChecked = false;
                    CopyNotificationOverlay.Visibility = Visibility.Visible;
                }
            }
            catch
            {
            }
        }
    }

    private void OnDismissCopyNotificationClicked(object sender, RoutedEventArgs e)
    {
        if (DontShowCopyNotificationCheck.IsChecked == true)
        {
            _settings.ShowClipboardNotification = false;
            ClipboardNotificationSwitch.IsChecked = false;
            SettingsService.Save(_settings);
        }

        CopyNotificationOverlay.Visibility = Visibility.Collapsed;
    }

    private async void OnChangeMasterPasswordClicked(object sender, RoutedEventArgs e)
    {
        if (_dbService == null || _derivedMasterKey == null) return;
        UpdateActivity();

        string currentPassword = ChangeCurrentPasswordBox.Password;
        string newPassword = ChangeNewPasswordBox.Password;
        string confirmNewPassword = ChangeConfirmPasswordBox.Password;

        if (newPassword.Length < 8 || newPassword != confirmNewPassword) return;

        try
        {
            byte[] currentSalt = await File.ReadAllBytesAsync(_saltFilePath);
            byte[] testKey = await _cryptoService.DeriveKeyAsync(currentPassword, currentSalt);

            bool isValid = _cryptoService.CompareKeys(testKey, _derivedMasterKey);
            CryptographicOperations.ZeroMemory(testKey);

            if (!isValid)
            {
                ChangePasswordStatusText.Foreground = ErrorBrush;
                ChangePasswordStatusText.Text = "Current password incorrect.";
                return;
            }

            var items = await _dbService.GetAllAsync();
            var reencryptedItems = new List<VaultItem>();

            byte[] newSalt = _cryptoService.GenerateSalt();
            byte[] newKey = await _cryptoService.DeriveKeyAsync(newPassword, newSalt);

            foreach (var item in items)
            {
                string decrypted = _cryptoService.Decrypt(item.EncryptedPassword, item.Nonce, item.AuthTag, _derivedMasterKey);
                var (newCiphertext, newNonce, newAuthTag) = _cryptoService.Encrypt(decrypted, newKey);

                reencryptedItems.Add(new VaultItem
                {
                    Id = item.Id,
                    EncryptedPassword = newCiphertext,
                    Nonce = newNonce,
                    AuthTag = newAuthTag
                });
            }

            await _dbService.ReencryptAllItemsAsync(reencryptedItems, newPassword);
            await File.WriteAllBytesAsync(_saltFilePath, newSalt);

            SetMasterKey(newKey);

            ChangeCurrentPasswordBox.Clear();
            ChangeNewPasswordBox.Clear();
            ChangeConfirmPasswordBox.Clear();

            ChangePasswordStatusText.Foreground = SuccessBrush;
            ChangePasswordStatusText.Text = "Password updated successfully.";
            await RefreshVaultListAsync();
        }
        catch (Exception ex)
        {
            ChangePasswordStatusText.Foreground = ErrorBrush;
            ChangePasswordStatusText.Text = $"Update failed: {ex.Message}";
        }
    }

    private void OnLockClicked(object sender, RoutedEventArgs e)
    {
        WipeSessionMemory();
        _dbService = null;
        VaultItemsContainer.ItemsSource = null;
        ConfigureViewState();
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        UpdateActivity();
        VaultPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
    }

    private void OnBackFromSettingsClicked(object sender, RoutedEventArgs e)
    {
        UpdateActivity();
        SettingsPanel.Visibility = Visibility.Collapsed;
        VaultPanel.Visibility = Visibility.Visible;
    }

    private void ApplyConfiguredTheme()
    {
        switch (_settings.Theme)
        {
            case "Light":
                LightThemeRadio.IsChecked = true;
                ApplyThemeDirect(ApplicationTheme.Light);
                break;
            case "Dark":
                DarkThemeRadio.IsChecked = true;
                ApplyThemeDirect(ApplicationTheme.Dark);
                break;
            default:
                SystemThemeRadio.IsChecked = true;
                ApplySystemTheme();
                break;
        }
    }

    private void ApplySystemTheme()
    {
        bool isLight = IsWindowsInLightMode();
        ApplyThemeDirect(isLight ? ApplicationTheme.Light : ApplicationTheme.Dark);
    }

    private static bool IsWindowsInLightMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
            return key?.GetValue("AppsUseLightTheme") is int val && val == 1;
        }
        catch { return false; }
    }

    private void ApplyThemeDirect(ApplicationTheme theme)
    {
        ApplicationThemeManager.Apply(theme, WindowBackdropType.None);
        ApplicationThemeManager.Apply(this);
    }

    private void OnSystemThemeChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.Theme = "System";
        SettingsService.Save(_settings);
        ApplySystemTheme();
    }

    private void OnDarkThemeChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.Theme = "Dark";
        SettingsService.Save(_settings);
        ApplyThemeDirect(ApplicationTheme.Dark);
    }

    private void OnLightThemeChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.Theme = "Light";
        SettingsService.Save(_settings);
        ApplyThemeDirect(ApplicationTheme.Light);
    }

    private void OnMinimizeToTrayToggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.MinimizeToTray = MinimizeToTraySwitch.IsChecked == true;
        SettingsService.Save(_settings);
    }

    private void OnStartAtLogonToggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SetStartup(StartAtLogonSwitch.IsChecked == true);
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("FennecGuard") != null;
        }
        catch { return false; }
    }

    private static void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            string? exe = Environment.ProcessPath;
            if (enable && !string.IsNullOrEmpty(exe))
                key.SetValue("FennecGuard", $"\"{exe}\"");
            else
                key.DeleteValue("FennecGuard", false);
        }
        catch { }
    }

    private void OnClipboardNotificationToggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.ShowClipboardNotification = ClipboardNotificationSwitch.IsChecked == true;
        SettingsService.Save(_settings);
    }

    private void OnAutoLockToggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool en = AutoLockSwitch.IsChecked == true;
        _settings.AutoLockEnabled = en;
        AutoLockHoursBox.IsEnabled = en;
        SettingsService.Save(_settings);
    }

    private void OnAutoLockHoursChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (int.TryParse(AutoLockHoursBox.Text.Trim(), out int h) && h > 0)
        {
            _settings.AutoLockHours = h;
            SettingsService.Save(_settings);
        }
    }

    private void SetMasterKey(byte[] newKey)
    {
        if (_derivedMasterKey != null) CryptographicOperations.ZeroMemory(_derivedMasterKey);
        _derivedMasterKey = GC.AllocateArray<byte>(newKey.Length, pinned: true);
        Buffer.BlockCopy(newKey, 0, _derivedMasterKey, 0, newKey.Length);
        CryptographicOperations.ZeroMemory(newKey);
    }

    private void WipeSessionMemory()
    {
        if (_derivedMasterKey != null)
        {
            CryptographicOperations.ZeroMemory(_derivedMasterKey);
            _derivedMasterKey = null;
        }

        _clipboardCts?.Cancel();
        _clipboardCts = null;

        if (_lastCopiedPassword != null)
        {
            if (Clipboard.GetText() == _lastCopiedPassword) Clipboard.Clear();
            _lastCopiedPassword = null;
        }
    }
}
