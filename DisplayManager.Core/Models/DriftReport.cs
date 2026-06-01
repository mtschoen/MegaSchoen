namespace DisplayManager.Core.Models;

/// <summary>Classification of a single monitor when comparing live config to a profile.</summary>
public enum DriftKind
{
    /// <summary>Live monitor matches the profile entry on every compared field.</summary>
    Match,
    /// <summary>Monitor matched by EDID, but one or more fields differ.</summary>
    FieldMismatch,
    /// <summary>Profile expects this monitor, but it is not currently active.</summary>
    MonitorNotConnected,
    /// <summary>Monitor is active in hardware but the profile does not mention it.</summary>
    UnexpectedActiveMonitor
}

/// <summary>Per-monitor drift entry.</summary>
public class MonitorDrift
{
    public string MonitorName { get; set; } = "";
    public int EdidManufactureId { get; set; }
    public int EdidProductCodeId { get; set; }
    public string EdidSerialNumber { get; set; } = "";
    public DriftKind Kind { get; set; }
    /// <summary>Human-readable field differences (empty unless Kind == FieldMismatch).</summary>
    public List<string> Mismatches { get; set; } = [];
}

/// <summary>Result of comparing the live display configuration to a saved profile.</summary>
public class DriftReport
{
    public List<MonitorDrift> Monitors { get; set; } = [];

    /// <summary>True only when every monitor entry is an exact Match (no missing/extra/mismatched).</summary>
    public bool Matches => Monitors.Count > 0 && Monitors.All(m => m.Kind == DriftKind.Match);
}
