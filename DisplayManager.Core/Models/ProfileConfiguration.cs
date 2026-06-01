namespace DisplayManager.Core.Models;

/// <summary>
/// Root configuration object that contains all profiles and settings.
/// This is the object serialized to/from configs.json. Named ...Configuration
/// (not ...Collection) because it is the config root, not an ICollection (CA1711).
/// </summary>
public class ProfileConfiguration
{
    /// <summary>
    /// Schema version for migration support.
    /// Current version: "1.0"
    /// </summary>
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>
    /// List of all saved display profiles.
    /// </summary>
    public List<SavedDisplayProfile> Profiles { get; set; } = [];

    /// <summary>
    /// Application-level settings.
    /// </summary>
    public ProfileSettings Settings { get; set; } = new();
}
