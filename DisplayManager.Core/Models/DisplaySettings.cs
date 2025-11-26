namespace DisplayManager.Core.Models
{
    /// <summary>
    /// Represents configuration settings for a single display in a profile.
    /// </summary>
    public class DisplaySettings
    {
        /// <summary>
        /// Identification information for matching this display to physical hardware.
        /// </summary>
        public DisplayIdentifier Identifier { get; set; } = new();

        /// <summary>
        /// Whether this display should be enabled in this configuration.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Whether this display is the primary display (Windows taskbar, login screen).
        /// </summary>
        public bool IsPrimary { get; set; }

        // Future Phase 5: Add DetailedDisplaySettings property for resolution, position, refresh rate
    }
}
