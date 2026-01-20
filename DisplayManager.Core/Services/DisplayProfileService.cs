using DisplayManager.Core.Models;

namespace DisplayManager.Core.Services;

/// <summary>
/// Service for saving and loading display profiles.
/// </summary>
public class DisplayProfileService
{
    readonly ProfileStorageService _storageService;

    public DisplayProfileService()
    {
        _storageService = new ProfileStorageService();
    }

    public DisplayProfileService(ProfileStorageService storageService)
    {
        _storageService = storageService;
    }

    /// <summary>
    /// Captures the current display configuration as a new profile.
    /// </summary>
    public SavedDisplayProfile CaptureCurrentConfiguration(string profileName, string? description = null)
    {
        var currentDisplays = DisplayManager.GetAllDisplays();
        var activeDisplayConfigs = currentDisplays
            .Where(d => d.IsActive && !string.IsNullOrEmpty(d.MonitorDevicePath))
            .Select(d => new SavedDisplayConfig
            {
                MonitorDevicePath = d.MonitorDevicePath,
                MonitorName = d.MonitorName,
                DeviceName = d.DeviceName,
                Width = d.Width,
                Height = d.Height,
                PositionX = d.PositionX,
                PositionY = d.PositionY,
                RefreshRate = d.RefreshRate,
                IsPrimary = d.IsPrimary
            })
            .ToList();

        return new SavedDisplayProfile
        {
            Name = profileName,
            Description = description ?? "",
            Displays = activeDisplayConfigs,
            Created = DateTime.UtcNow,
            LastModified = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Saves a profile.
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
    /// Applies a saved profile using DisplayManager.ApplyConfiguration.
    /// </summary>
    public ApplyResult ApplyProfile(SavedDisplayProfile profile)
    {
        return DisplayManager.ApplyConfiguration(profile.Displays);
    }
}
