namespace DisplayManager.Core.Models;

/// <summary>
/// Represents the identification information for a display monitor.
/// Uses multi-tiered matching strategy for robustness across hardware changes.
/// </summary>
public class DisplayIdentifier
{
    /// <summary>
    /// Hardware-specific monitor instance path (most specific, but may change on reconnect).
    /// Example: "MONITOR\\DEL1234\\{4d36e96e-e325-11ce-bfc1-08002be10318}\\0001"
    /// </summary>
    public string MonitorId { get; set; } = "";

    /// <summary>
    /// Windows device name (changes based on connection order).
    /// Example: "\\\\.\\DISPLAY1"
    /// </summary>
    public string DeviceName { get; set; } = "";

    /// <summary>
    /// User-friendly monitor name from EDID.
    /// Example: "Dell U2720Q", "LG OLED TV"
    /// </summary>
    public string MonitorName { get; set; } = "";

    /// <summary>
    /// Specifies which identifier to use as fallback when primary match fails.
    /// Valid values: "monitorId", "deviceName", "monitorName"
    /// </summary>
    public string FallbackMatch { get; set; } = "deviceName";
}
