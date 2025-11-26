using DisplayManager.Core.Models;

namespace DisplayManager.Core.Services
{
    /// <summary>
    /// Service for matching saved display configurations to current physical displays.
    /// Uses multi-tiered matching strategy to handle hardware changes.
    /// </summary>
    public class DisplayMatchingService
    {
        /// <summary>
        /// Attempts to find a matching physical display for a saved display configuration.
        /// Returns null if no match is found.
        /// </summary>
        public DisplayInfo? FindMatchingDisplay(DisplaySettings savedDisplay, List<DisplayInfo> currentDisplays)
        {
            var identifier = savedDisplay.Identifier;

            // Strategy 1: Try MonitorId match first (most specific)
            if (!string.IsNullOrEmpty(identifier.MonitorId))
            {
                var match = currentDisplays.FirstOrDefault(d =>
                    d.MonitorID.Equals(identifier.MonitorId, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            // Strategy 2: Use the fallback strategy specified in the identifier
            return identifier.FallbackMatch?.ToLower() switch
            {
                "monitorid" => FindByMonitorId(identifier.MonitorId, currentDisplays),
                "devicename" => FindByDeviceName(identifier.DeviceName, currentDisplays),
                "monitorname" => FindByMonitorName(identifier.MonitorName, currentDisplays),
                _ => FindByDeviceName(identifier.DeviceName, currentDisplays) // Default fallback
            };
        }

        /// <summary>
        /// Validates whether a saved profile can be applied to the current display configuration.
        /// Returns a list of warnings/issues found.
        /// </summary>
        public List<string> ValidateProfile(SavedDisplayProfile profile, List<DisplayInfo> currentDisplays)
        {
            var warnings = new List<string>();

            foreach (var savedDisplay in profile.Displays.Where(d => d.Enabled))
            {
                var match = FindMatchingDisplay(savedDisplay, currentDisplays);
                if (match == null)
                {
                    warnings.Add($"Display '{savedDisplay.Identifier.MonitorName}' not found in current configuration");
                }
            }

            // Check for primary display
            var hasPrimary = profile.Displays.Any(d => d.Enabled && d.IsPrimary);
            if (!hasPrimary && profile.Displays.Any(d => d.Enabled))
            {
                warnings.Add("No primary display specified in profile");
            }

            return warnings;
        }

        private DisplayInfo? FindByMonitorId(string monitorId, List<DisplayInfo> displays)
        {
            if (string.IsNullOrEmpty(monitorId)) return null;

            return displays.FirstOrDefault(d =>
                d.MonitorID.Equals(monitorId, StringComparison.OrdinalIgnoreCase));
        }

        private DisplayInfo? FindByDeviceName(string deviceName, List<DisplayInfo> displays)
        {
            if (string.IsNullOrEmpty(deviceName)) return null;

            return displays.FirstOrDefault(d =>
                d.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
        }

        private DisplayInfo? FindByMonitorName(string monitorName, List<DisplayInfo> displays)
        {
            if (string.IsNullOrEmpty(monitorName)) return null;

            return displays.FirstOrDefault(d =>
                d.MonitorName.Equals(monitorName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
