using System.IO;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace PasswordManager.Desktop.Services;

public class TrayService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly Action _onRestore;
    private readonly Action _onLock;
    private readonly Action _onExit;

    public TrayService(Action onRestore, Action onLock, Action onExit)
    {
        _onRestore = onRestore;
        _onLock = onLock;
        _onExit = onExit;

        InitializeTrayIcon();
    }

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
        menu.Items.Add("Open FennecGuard", null, (s, e) => Application.Current.Dispatcher.Invoke(_onRestore));
        menu.Items.Add("Lock Vault", null, (s, e) => Application.Current.Dispatcher.Invoke(_onLock));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Application.Current.Dispatcher.Invoke(_onExit));

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (s, e) => Application.Current.Dispatcher.Invoke(_onRestore);
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
