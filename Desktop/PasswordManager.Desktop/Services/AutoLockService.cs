using System.Windows.Threading;

namespace PasswordManager.Desktop.Services;

public class AutoLockService
{
    private readonly DispatcherTimer _timer;
    private readonly Action _onLockTriggered;
    private DateTime _lastActivity = DateTime.UtcNow;

    public bool IsEnabled { get; set; } = true;
    public int TimeoutHours { get; set; } = 24;

    public AutoLockService(Action onLockTriggered)
    {
        _onLockTriggered = onLockTriggered;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _timer.Tick += (s, e) => CheckTimeout();
        _timer.Start();
    }

    public void RecordActivity()
    {
        _lastActivity = DateTime.UtcNow;
    }

    private void CheckTimeout()
    {
        if (!IsEnabled) return;

        var elapsed = DateTime.UtcNow - _lastActivity;
        if (elapsed.TotalHours >= TimeoutHours)
        {
            _onLockTriggered();
        }
    }
}
