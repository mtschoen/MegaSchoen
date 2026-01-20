namespace DisplayManager.Core.Models;

/// <summary>
/// Display configuration saved in a profile.
/// </summary>
public class SavedDisplayConfig
{
    /// <summary>
    /// Hardware path that uniquely identifies the monitor (stable across reboots).
    /// </summary>
    public string MonitorDevicePath { get; set; } = "";

    /// <summary>
    /// Friendly monitor name from EDID.
    /// </summary>
    public string MonitorName { get; set; } = "";

    /// <summary>
    /// GDI device name at time of capture (may change between sessions).
    /// </summary>
    public string DeviceName { get; set; } = "";

    public int Width { get; set; }
    public int Height { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public double RefreshRate { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>
/// A saved display profile - a full display configuration to restore.
/// </summary>
public class SavedDisplayProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>
    /// Full configuration of displays that should be active.
    /// Displays not in this list will be disabled when the profile is applied.
    /// </summary>
    public List<SavedDisplayConfig> Displays { get; set; } = [];

    /// <summary>
    /// Optional hotkey for activating this profile.
    /// </summary>
    public HotkeyDefinition? Hotkey { get; set; }

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}
