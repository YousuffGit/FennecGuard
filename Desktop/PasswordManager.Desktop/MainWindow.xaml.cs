using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using PasswordManager.Desktop.Models;
using PasswordManager.Desktop.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PasswordManager.Desktop;

public partial class MainWindow : FluentWindow
{
    private readonly CryptoService _cryptoService = new();
    private DatabaseService? _dbService;
    private byte[]? _derivedMasterKey;
    private string? _activeMasterPassword;
    private CancellationTokenSource? _clipboardCts;

    private readonly string _saltFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vault.salt");
    private readonly string _dbFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vault.db");

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DarkThemeRadio.IsChecked = true;
        ConfigureViewState();
    }

    // Configures the window layout and position based on whether the vault is locked
    private void ConfigureViewState()
    {
        bool isFirstRun = !File.Exists(_dbFilePath) || !File.Exists(_saltFilePath);

        var workArea = SystemParameters.WorkArea;

        if (isFirstRun)
        {
            // Center the setup window on screen
            Width = 520;
            Height = 520;
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Top + (workArea.Height - Height) / 2;

            SetupPanel.Visibility = Visibility.Visible;
            UnlockPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Position as a compact popup in the bottom-right corner
            Width = 340;
            Height = 260;
            Left = workArea.Right - Width - 24;
            Top = workArea.Bottom - Height - 24;

            SetupPanel.Visibility = Visibility.Collapsed;
            UnlockPanel.Visibility = Visibility.Visible;
            UnlockPasswordBox.Focus();
        }

        VaultPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        SetupMasterPasswordBox.Clear();
        SetupConfirmPasswordBox.Clear();
        UnlockPasswordBox.Clear();
        UnlockStatusText.Text = string.Empty;
        SetupStatusText.Text = string.Empty;
    }

    // Expands and centers window upon successful unlock
    private void TransitionToVaultView()
    {
        var workArea = SystemParameters.WorkArea;
        Width = 1020;
        Height = 720;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;

        UnlockPanel.Visibility = Visibility.Collapsed;
        SetupPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        VaultPanel.Visibility = Visibility.Visible;
    }

    private async void OnInitializeVaultClicked(object sender, RoutedEventArgs e)
    {
        string password = SetupMasterPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            SetupStatusText.Text = "Password must be at least 8 characters.";
            return;
        }

        if (password != SetupConfirmPasswordBox.Password)
        {
            SetupStatusText.Text = "Passwords do not match.";
            return;
        }

        try
        {
            byte[] salt = _cryptoService.GenerateSalt();
            await File.WriteAllBytesAsync(_saltFilePath, salt);

            _derivedMasterKey = await _cryptoService.DeriveKeyAsync(password, salt);
            _activeMasterPassword = password;
            _dbService = new DatabaseService(_dbFilePath, password);

            await _dbService.InitializeDatabaseAsync();
            await RefreshVaultListAsync();

            TransitionToVaultView();
            SetupMasterPasswordBox.Clear();
            SetupConfirmPasswordBox.Clear();
        }
        catch (Exception ex)
        {
            SetupStatusText.Text = $"Error initializing vault: {ex.Message}";
        }
    }

    private void OnUnlockKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnUnlockClicked(sender, e);
        }
    }

    private async void OnUnlockClicked(object sender, RoutedEventArgs e)
    {
        string password = UnlockPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(password))
        {
            UnlockStatusText.Text = "Enter master password.";
            return;
        }

        UnlockButton.IsEnabled = false;
        UnlockStatusText.Text = "Unlocking...";

        try
        {
            byte[] salt = await File.ReadAllBytesAsync(_saltFilePath);
            byte[] testKey = await _cryptoService.DeriveKeyAsync(password, salt);

            var testDb = new DatabaseService(_dbFilePath, password);
            var items = await testDb.GetAllAsync();

            // Validate decryption against first stored credential
            if (items.Count > 0)
            {
                var testItem = items[0];
                _cryptoService.Decrypt(testItem.EncryptedPassword, testItem.Nonce, testItem.AuthTag, testKey);
            }

            _derivedMasterKey = testKey;
            _activeMasterPassword = password;
            _dbService = testDb;

            VaultItemsContainer.ItemsSource = items;
            EmptyVaultText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            TransitionToVaultView();
            UnlockPasswordBox.Clear();
            UnlockStatusText.Text = string.Empty;
        }
        catch (SqliteException)
        {
            UnlockStatusText.Text = "Incorrect password.";
        }
        catch (CryptographicException)
        {
            UnlockStatusText.Text = "Incorrect password.";
        }
        catch (Exception ex)
        {
            UnlockStatusText.Text = $"Error: {ex.Message}";
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

        string title = NewTitleBox.Text.Trim();
        string username = NewUsernameBox.Text.Trim();
        string url = NewUrlBox.Text.Trim();
        string plainPassword = NewPasswordBox.Password;

        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(plainPassword))
        {
            System.Windows.MessageBox.Show(
                "Title and Password are required.",
                "Validation",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

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
            try
            {
                string decrypted = _cryptoService.Decrypt(item.EncryptedPassword, item.Nonce, item.AuthTag, _derivedMasterKey);
                Clipboard.SetText(decrypted);

                // Auto-clear clipboard after 30 seconds
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
                            if (Clipboard.GetText() == decrypted)
                            {
                                Clipboard.Clear();
                            }
                        });
                    }
                }, token);

                System.Windows.MessageBox.Show(
                    $"Password for '{item.Title}' copied to clipboard.\nAuto-clears in 30 seconds.",
                    "Copied",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch
            {
                System.Windows.MessageBox.Show(
                    "Failed to decrypt password.",
                    "Decryption Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }

    private async void OnChangeMasterPasswordClicked(object sender, RoutedEventArgs e)
    {
        if (_dbService == null || _derivedMasterKey == null || _activeMasterPassword == null) return;

        string currentPassword = ChangeCurrentPasswordBox.Password;
        string newPassword = ChangeNewPasswordBox.Password;
        string confirmNewPassword = ChangeConfirmPasswordBox.Password;

        if (currentPassword != _activeMasterPassword)
        {
            ChangePasswordStatusText.Foreground = System.Windows.Media.Brushes.Red;
            ChangePasswordStatusText.Text = "Current master password is incorrect.";
            return;
        }

        if (newPassword.Length < 8)
        {
            ChangePasswordStatusText.Foreground = System.Windows.Media.Brushes.Red;
            ChangePasswordStatusText.Text = "New password must be at least 8 characters.";
            return;
        }

        if (newPassword != confirmNewPassword)
        {
            ChangePasswordStatusText.Foreground = System.Windows.Media.Brushes.Red;
            ChangePasswordStatusText.Text = "Passwords do not match.";
            return;
        }

        try
        {
            var items = await _dbService.GetAllAsync();
            var reencryptedItems = new List<VaultItem>();

            byte[] newSalt = _cryptoService.GenerateSalt();
            byte[] newKey = await _cryptoService.DeriveKeyAsync(newPassword, newSalt);

            // Re-encrypt each stored password under the new key
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

            CryptographicOperations.ZeroMemory(_derivedMasterKey);
            _derivedMasterKey = newKey;
            _activeMasterPassword = newPassword;

            ChangeCurrentPasswordBox.Clear();
            ChangeNewPasswordBox.Clear();
            ChangeConfirmPasswordBox.Clear();

            ChangePasswordStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            ChangePasswordStatusText.Text = "Master password updated successfully.";
            await RefreshVaultListAsync();
        }
        catch (Exception ex)
        {
            ChangePasswordStatusText.Foreground = System.Windows.Media.Brushes.Red;
            ChangePasswordStatusText.Text = $"Update failed: {ex.Message}";
        }
    }

    private void OnLockClicked(object sender, RoutedEventArgs e)
    {
        // Zero master key buffer in memory
        if (_derivedMasterKey != null)
        {
            CryptographicOperations.ZeroMemory(_derivedMasterKey);
            _derivedMasterKey = null;
        }

        _activeMasterPassword = null;
        _dbService = null;
        VaultItemsContainer.ItemsSource = null;

        ConfigureViewState();
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        VaultPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        ChangePasswordStatusText.Text = string.Empty;
        ChangeCurrentPasswordBox.Clear();
        ChangeNewPasswordBox.Clear();
        ChangeConfirmPasswordBox.Clear();
    }

    private void OnBackFromSettingsClicked(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        VaultPanel.Visibility = Visibility.Visible;
    }

    private void SwitchTheme(ApplicationTheme theme)
    {
        if (!IsLoaded) return;
        ApplicationThemeManager.Apply(theme, WindowBackdropType.None);
        ApplicationThemeManager.Apply(this);
    }

    private void OnDarkThemeChecked(object sender, RoutedEventArgs e)
    {
        SwitchTheme(ApplicationTheme.Dark);
    }

    private void OnLightThemeChecked(object sender, RoutedEventArgs e)
    {
        SwitchTheme(ApplicationTheme.Light);
    }
}
