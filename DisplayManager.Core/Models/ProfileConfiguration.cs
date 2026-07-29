namespace DisplayManager.Core.Models;

/// <summary>
/// Root object serialized to and from configs.json.
/// </summary>
public class ProfileConfiguration
{
    /// <summary>
    /// List of all saved display profiles.
    /// </summary>
    public List<SavedDisplayProfile> Profiles { get; set; } = [];
}
