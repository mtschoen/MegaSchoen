namespace DisplayManager.Core.Models;

/// <summary>
/// Application-level settings for profile management.
/// </summary>
public class ProfileSettings
{
    /// <summary>
    /// ID of the default profile to load on startup (null = none).
    /// </summary>
    public Guid? DefaultProfileId { get; set; }

    /// <summary>
    /// Whether to automatically load the default profile on application startup.
    /// </summary>
    public bool AutoLoadOnStartup { get; set; }

    /// <summary>
    /// Whether to show system tray notifications when profiles are activated.
    /// </summary>
    public bool ShowTrayNotifications { get; set; } = true;

    /// <summary>
    /// Whether to minimize MAUI app to system tray instead of taskbar.
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Whether to start the application when Windows starts.
    /// </summary>
    public bool StartWithWindows { get; set; }
}
