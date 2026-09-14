namespace PasswordManager.Desktop.Models;

public class AppSettings
{
    public string Theme { get; set; } = "System"; // "System", "Dark", "Light"
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoLockEnabled { get; set; } = true;
    public int AutoLockHours { get; set; } = 24;
    public bool ShowClipboardNotification { get; set; } = true;
}
