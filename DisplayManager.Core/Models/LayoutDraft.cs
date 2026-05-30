namespace DisplayManager.Core.Models;

/// <summary>
/// An in-progress edit of a preset's layout. Stored separately from configs.json;
/// may be invalid. <see cref="VerifiedHash"/> records the LayoutHasher hash that last
/// passed a Test (apply + no drift); commit is allowed only when it equals the current
/// draft's hash.
/// </summary>
public class LayoutDraft
{
    public Guid PresetId { get; set; }
    public string PresetName { get; set; } = "";
    public List<SavedDisplayConfig> Displays { get; set; } = [];

    /// <summary>Hash of the draft content that last passed Test; empty if never verified.</summary>
    public string VerifiedHash { get; set; } = "";

    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}
