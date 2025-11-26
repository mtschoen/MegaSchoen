namespace DisplayManager.Core.Models
{
    /// <summary>
    /// Represents a saved display configuration profile that can be applied via hotkey or UI.
    /// </summary>
    public class SavedDisplayProfile
    {
        /// <summary>
        /// Unique identifier for this profile.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// User-friendly name for the profile (e.g., "Desk Mode", "TV Mode").
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Optional description of what this profile does.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Type of configuration: "topology" (simple) or "detailed" (Phase 5).
        /// </summary>
        public string ConfigType { get; set; } = "topology";

        /// <summary>
        /// Windows display topology for simple configuration.
        /// Valid values: "internal", "extend", "clone", "external"
        /// Only used when ConfigType = "topology".
        /// </summary>
        public string? Topology { get; set; }

        /// <summary>
        /// List of display configurations in this profile.
        /// </summary>
        public List<DisplaySettings> Displays { get; set; } = new();

        /// <summary>
        /// Global hotkey definition for activating this profile.
        /// </summary>
        public HotkeyDefinition? Hotkey { get; set; }

        /// <summary>
        /// Timestamp when this profile was created.
        /// </summary>
        public DateTime Created { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when this profile was last modified.
        /// </summary>
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }
}
