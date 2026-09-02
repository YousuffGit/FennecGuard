using System.Windows;

namespace PasswordManager.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (sender, args) =>
        {
            MessageBox.Show(
                $"Startup Error:\n\n{args.Exception.Message}\n\n{args.Exception.InnerException?.Message}",
                "FennecGuard Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
