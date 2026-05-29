namespace DisplayManager.Core.Models;

/// <summary>
/// Root configuration object that contains all profiles and settings.
/// This is the object serialized to/from configs.json.
/// </summary>
public class ProfileCollection
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
