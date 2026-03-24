namespace DisplayManager.Core;

public class DisplayInfo
{
    // Core identification
    public string DeviceName { get; set; } = "";  // e.g., \\.\DISPLAY1
    public string MonitorName { get; set; } = ""; // Friendly name from EDID
    public string MonitorDevicePath { get; set; } = ""; // Full device path

    // Display state
    public bool IsActive { get; set; }
    public bool IsPrimary { get; set; }
    public bool TargetAvailable { get; set; }

    // Resolution and position
    public int Width { get; set; }
    public int Height { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public double RefreshRate { get; set; }
    public int Rotation { get; set; }  // degrees: 0, 90, 180, 270

    // EDID identification (stable across GPU swaps)
    public int EdidManufactureId { get; set; }
    public int EdidProductCodeId { get; set; }
    public string EdidSerialNumber { get; set; } = "";
    public string EdidManufactureDate { get; set; } = "";
    public string EdidContainerId { get; set; } = "";

    // CCD path identifiers (for internal use)
    public int PathIndex { get; set; }
    public int SourceId { get; set; }
    public int TargetId { get; set; }
}
