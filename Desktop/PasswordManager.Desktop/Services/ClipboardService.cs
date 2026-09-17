using System.Runtime.InteropServices;
using System.Windows;
using Clipboard = System.Windows.Clipboard;

namespace PasswordManager.Desktop.Services;

public class ClipboardService : IDisposable
{
    private string? _lastCopiedPassword;
    private CancellationTokenSource? _clipboardCts;

    // Copies password with retry loop to handle Win32 COMException clipboard locking
    public void CopyPassword(string password)
    {
        _lastCopiedPassword = password;

        for (int i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetText(password);
                break;
            }
            catch (COMException)
            {
                Thread.Sleep(50);
            }
        }

        // Schedule auto-wipe after 30 seconds
        _clipboardCts?.Cancel();
        _clipboardCts = new CancellationTokenSource();
        var token = _clipboardCts.Token;

        Task.Run(async () =>
        {
            await Task.Delay(30000, token);
            if (!token.IsCancellationRequested)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    WipeIfMatching();
                });
            }
        }, token);
    }

    public void WipeIfMatching()
    {
        try
        {
            if (_lastCopiedPassword != null && Clipboard.GetText() == _lastCopiedPassword)
            {
                Clipboard.Clear();
            }
        }
        catch (COMException)
        {
            // Ignore clipboard access contention during wipe
        }
        finally
        {
            _lastCopiedPassword = null;
        }
    }

    public void Dispose()
    {
        _clipboardCts?.Cancel();
        _clipboardCts = null;
        WipeIfMatching();
    }
}
