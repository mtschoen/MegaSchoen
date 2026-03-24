namespace DisplayManager.Core.Models;

/// <summary>
/// Display configuration saved in a profile.
/// </summary>
public class SavedDisplayConfig
{
    // --- Hardware identification (stable across GPU swaps / port changes) ---

    /// <summary>Friendly monitor name from EDID (e.g. "XB271HK").</summary>
    public string MonitorName { get; set; } = "";

    /// <summary>EDID manufacturer ID.</summary>
    public int EdidManufactureId { get; set; }

    /// <summary>EDID product code.</summary>
    public int EdidProductCodeId { get; set; }

    /// <summary>EDID serial number string.</summary>
    public string EdidSerialNumber { get; set; } = "";

    /// <summary>EDID manufacture date "YYYY-WNN".</summary>
    public string EdidManufactureDate { get; set; } = "";

    /// <summary>DisplayID Container ID (128-bit UUID hex).</summary>
    public string EdidContainerId { get; set; } = "";

    // --- Display configuration ---

    public int Width { get; set; }
    public int Height { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public double RefreshRate { get; set; }
    public int Rotation { get; set; }  // degrees: 0, 90, 180, 270
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
