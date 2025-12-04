using DisplayManager.Core.Models;
using System.Text.Json;

namespace DisplayManager.Core.Services
{
    /// <summary>
    /// Service for creating and managing display profiles from current display state.
    /// </summary>
    public class DisplayProfileService
    {
        private readonly ProfileStorageService _storageService;

        public DisplayProfileService()
        {
            _storageService = new ProfileStorageService();
        }

        public DisplayProfileService(ProfileStorageService storageService)
        {
            _storageService = storageService;
        }

        /// <summary>
        /// Captures the current display configuration and creates a new profile.
        /// </summary>
        /// <param name="profileName">User-friendly name for the profile</param>
        /// <param name="description">Optional description</param>
        /// <returns>The created profile</returns>
        public SavedDisplayProfile CaptureCurrentConfiguration(string profileName, string? description = null)
        {
            var currentDisplays = DisplayManager.GetAllDisplays();

            // Debug: Log what we're capturing
            System.Diagnostics.Debug.WriteLine("=== CAPTURING PROFILE ===");
            System.Diagnostics.Debug.WriteLine($"Profile: {profileName}");
            System.Diagnostics.Debug.WriteLine($"Found {currentDisplays.Count} displays:");
            foreach (var d in currentDisplays)
            {
                System.Diagnostics.Debug.WriteLine($"  - {d.MonitorName} ({d.DeviceName})");
                System.Diagnostics.Debug.WriteLine($"    MonitorID: {d.MonitorID}");
                System.Diagnostics.Debug.WriteLine($"    IsActive: {d.IsActive}");
                System.Diagnostics.Debug.WriteLine($"    IsPrimary: {d.IsPrimary}");
            }

            var profile = new SavedDisplayProfile
            {
                Name = profileName,
                Description = description ?? "",
                ConfigType = "detailed",
                Displays = currentDisplays.Select(d => new DisplaySettings
                {
                    Identifier = new DisplayIdentifier
                    {
                        MonitorId = d.MonitorID,
                        DeviceName = d.DeviceName,
                        MonitorName = d.MonitorName,
                        FallbackMatch = "monitorName"
                    },
                    Enabled = d.IsActive,
                    IsPrimary = d.IsPrimary
                }).ToList(),
                Created = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            };

            return profile;
        }

        /// <summary>
        /// Saves a profile to the profile collection.
        /// </summary>
        public async Task SaveProfileAsync(SavedDisplayProfile profile)
        {
            var collection = await _storageService.LoadAsync();

            // Check if profile with same ID exists and update, otherwise add
            var existingIndex = collection.Profiles.FindIndex(p => p.Id == profile.Id);
            if (existingIndex >= 0)
            {
                profile.LastModified = DateTime.UtcNow;
                collection.Profiles[existingIndex] = profile;
            }
            else
            {
                collection.Profiles.Add(profile);
            }

            await _storageService.SaveAsync(collection);
        }

        /// <summary>
        /// Gets all saved profiles.
        /// </summary>
        public async Task<List<SavedDisplayProfile>> GetAllProfilesAsync()
        {
            var collection = await _storageService.LoadAsync();
            return collection.Profiles;
        }

        /// <summary>
        /// Deletes a profile by ID.
        /// </summary>
        public async Task DeleteProfileAsync(Guid profileId)
        {
            var collection = await _storageService.LoadAsync();
            collection.Profiles.RemoveAll(p => p.Id == profileId);
            await _storageService.SaveAsync(collection);
        }

        /// <summary>
        /// Gets a profile by ID.
        /// </summary>
        public async Task<SavedDisplayProfile?> GetProfileByIdAsync(Guid profileId)
        {
            var collection = await _storageService.LoadAsync();
            return collection.Profiles.FirstOrDefault(p => p.Id == profileId);
        }

        /// <summary>
        /// Applies a saved profile to the system.
        /// </summary>
        /// <param name="profile">The profile to apply</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool ApplyProfile(SavedDisplayProfile profile)
        {
            // Convert profile to JSON format expected by native code
            var config = new
            {
                displays = profile.Displays.Select(d => new
                {
                    enabled = d.Enabled,
                    isPrimary = d.IsPrimary,
                    identifier = new
                    {
                        monitorId = d.Identifier.MonitorId,
                        deviceName = d.Identifier.DeviceName,
                        monitorName = d.Identifier.MonitorName
                    }
                }).ToArray()
            };

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            string jsonConfig = JsonSerializer.Serialize(config, options);

            // Debug: Log what we're sending
            System.Diagnostics.Debug.WriteLine("=== APPLYING PROFILE ===");
            System.Diagnostics.Debug.WriteLine($"Profile: {profile.Name}");
            System.Diagnostics.Debug.WriteLine($"Config JSON:\n{jsonConfig}");

            // Call the native function to apply the configuration
            bool result = DisplayManager.ApplyDisplayConfiguration(jsonConfig);

            System.Diagnostics.Debug.WriteLine($"Result: {result}");

            return result;
        }
    }
}
