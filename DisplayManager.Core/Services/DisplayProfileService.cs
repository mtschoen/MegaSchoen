using DisplayManager.Core.Models;

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
        public SavedDisplayProfile CaptureCurrentConfiguration(string profileName, string? description = null)
        {
            var currentDisplays = DisplayManager.GetAllDisplays()
                .Where(d => !string.IsNullOrEmpty(d.DeviceName))
                .ToList();

            var profile = new SavedDisplayProfile
            {
                Name = profileName,
                Description = description ?? "",
                ConfigType = "detailed",
                Displays = currentDisplays.Select(d => new DisplaySettings
                {
                    Identifier = new DisplayIdentifier
                    {
                        MonitorId = d.MonitorDevicePath,
                        DeviceName = d.DeviceName,
                        MonitorName = d.MonitorName,
                        FallbackMatch = "deviceName"
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
        /// Applies a saved profile to the system using the CCD API.
        /// </summary>
        public bool ApplyProfile(SavedDisplayProfile profile)
        {
            bool allSuccess = true;

            foreach (var displaySetting in profile.Displays)
            {
                string deviceName = displaySetting.Identifier.DeviceName;
                if (string.IsNullOrEmpty(deviceName)) continue;

                int result = DisplayManager.ToggleDisplay(deviceName, displaySetting.Enabled);
                if (result != 0)
                {
                    allSuccess = false;
                }
            }

            return allSuccess;
        }
    }
}
