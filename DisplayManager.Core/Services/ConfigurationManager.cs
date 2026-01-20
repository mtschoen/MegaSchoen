using DisplayManager.Core.Models;

namespace DisplayManager.Core.Services;

/// <summary>
/// Main service for managing display configuration profiles.
/// Handles capturing current configurations and applying saved ones.
/// </summary>
public class ConfigurationManager
{
    private readonly ProfileStorageService _storage;
    private ProfileCollection? _cachedCollection;

    public ConfigurationManager(ProfileStorageService storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// Captures the current display configuration and saves it as a new profile.
    /// </summary>
    public async Task<SavedDisplayProfile> CaptureCurrentConfigurationAsync(string name, string? description = null)
    {
        // Get current display information
        var displays = DisplayManager.GetAllDisplays();

        // Determine current topology (Phase 2: will detect actual topology)
        // For now, assume "extend" if multiple active displays
        var activeDisplays = displays.Where(d => d.IsActive).ToList();
        var topology = activeDisplays.Count > 1 ? "extend" : "internal";

        var profile = new SavedDisplayProfile
        {
            Name = name,
            Description = description ?? "",
            ConfigType = "topology",
            Topology = topology,
            Displays = displays
                .Where(d => !string.IsNullOrEmpty(d.DeviceName))
                .Select(d => new DisplaySettings
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
                }).ToList()
        };

        // Save the profile
        await AddProfileAsync(profile);

        return profile;
    }

    /// <summary>
    /// Applies a saved profile by ID.
    /// </summary>
    public async Task<bool> ApplyProfileAsync(Guid profileId)
    {
        var collection = await GetCollectionAsync();
        var profile = collection.Profiles.FirstOrDefault(p => p.Id == profileId);

        if (profile == null)
        {
            return false;
        }

        return ApplyProfile(profile);
    }

    /// <summary>
    /// Applies a saved profile directly.
    /// </summary>
    public bool ApplyProfile(SavedDisplayProfile profile)
    {
        if (profile.ConfigType == "topology")
        {
            return ApplyTopologyConfiguration(profile);
        }

        // Phase 5: Add detailed configuration support
        return false;
    }

    /// <summary>
    /// Applies a topology-based configuration using Windows display topology modes.
    /// </summary>
    private bool ApplyTopologyConfiguration(SavedDisplayProfile profile)
    {
        return profile.Topology?.ToLower() switch
        {
            "internal" => DisplayManager.SwitchToInternalDisplay(),
            "extend" => DisplayManager.EnableAllDisplays(),
            // Phase 2: Add "clone" and "external" support
            _ => false
        };
    }

    /// <summary>
    /// Adds a new profile to the collection.
    /// </summary>
    public async Task AddProfileAsync(SavedDisplayProfile profile)
    {
        var collection = await GetCollectionAsync();

        // Update timestamps
        profile.Created = DateTime.UtcNow;
        profile.LastModified = DateTime.UtcNow;

        // Add to collection
        collection.Profiles.Add(profile);

        // Save
        await _storage.SaveAsync(collection);
        _cachedCollection = collection;
    }

    /// <summary>
    /// Updates an existing profile.
    /// </summary>
    public async Task<bool> UpdateProfileAsync(SavedDisplayProfile profile)
    {
        var collection = await GetCollectionAsync();
        var existing = collection.Profiles.FirstOrDefault(p => p.Id == profile.Id);

        if (existing == null)
        {
            return false;
        }

        // Update timestamp
        profile.LastModified = DateTime.UtcNow;

        // Replace in collection
        var index = collection.Profiles.IndexOf(existing);
        collection.Profiles[index] = profile;

        // Save
        await _storage.SaveAsync(collection);
        _cachedCollection = collection;

        return true;
    }

    /// <summary>
    /// Deletes a profile by ID.
    /// </summary>
    public async Task<bool> DeleteProfileAsync(Guid profileId)
    {
        var collection = await GetCollectionAsync();
        var profile = collection.Profiles.FirstOrDefault(p => p.Id == profileId);

        if (profile == null)
        {
            return false;
        }

        collection.Profiles.Remove(profile);
        await _storage.SaveAsync(collection);
        _cachedCollection = collection;

        return true;
    }

    /// <summary>
    /// Gets all saved profiles.
    /// </summary>
    public async Task<List<SavedDisplayProfile>> GetAllProfilesAsync()
    {
        var collection = await GetCollectionAsync();
        return collection.Profiles;
    }

    /// <summary>
    /// Gets a profile by ID.
    /// </summary>
    public async Task<SavedDisplayProfile?> GetProfileAsync(Guid profileId)
    {
        var collection = await GetCollectionAsync();
        return collection.Profiles.FirstOrDefault(p => p.Id == profileId);
    }

    /// <summary>
    /// Gets a profile by name.
    /// </summary>
    public async Task<SavedDisplayProfile?> GetProfileByNameAsync(string name)
    {
        var collection = await GetCollectionAsync();
        return collection.Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the profile collection, loading from disk if necessary.
    /// </summary>
    private async Task<ProfileCollection> GetCollectionAsync()
    {
        if (_cachedCollection == null)
        {
            _cachedCollection = await _storage.LoadAsync();
        }
        return _cachedCollection;
    }

    /// <summary>
    /// Reloads the profile collection from disk (clears cache).
    /// </summary>
    public async Task ReloadAsync()
    {
        _cachedCollection = await _storage.LoadAsync();
    }
}
