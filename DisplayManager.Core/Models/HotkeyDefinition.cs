namespace DisplayManager.Core.Models;

/// <summary>
/// Represents a global hotkey configuration in a platform-agnostic format.
/// </summary>
public class HotkeyDefinition
{
    /// <summary>
    /// List of modifier keys required for this hotkey.
    /// Valid values: "Control", "Alt", "Shift", "Win"
    /// </summary>
    public List<string> Modifiers { get; set; } = [];

    /// <summary>
    /// The primary key for the hotkey (e.g., "D", "F1", "Escape").
    /// Uses standard key names compatible across platforms.
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Whether this hotkey is currently enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
