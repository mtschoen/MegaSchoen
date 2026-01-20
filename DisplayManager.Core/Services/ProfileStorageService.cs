using System.Text.Json;
using DisplayManager.Core.Models;

namespace DisplayManager.Core.Services;

/// <summary>
/// Handles file I/O operations for saving and loading profile configurations.
/// Implements atomic writes and automatic backups.
/// </summary>
public class ProfileStorageService
{
    readonly string _configDirectory;
    readonly string _configFilePath;
    readonly JsonSerializerOptions _jsonOptions;

    public ProfileStorageService()
    {
        // Store in %APPDATA%\MegaSchoen\
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _configDirectory = Path.Combine(appDataPath, "MegaSchoen");
        _configFilePath = Path.Combine(_configDirectory, "configs.json");

        // Ensure directory exists
        Directory.CreateDirectory(_configDirectory);

        // Configure JSON serialization for human-readable output
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// Loads the profile collection from disk.
    /// Returns a new empty collection if the file doesn't exist.
    /// </summary>
    public async Task<ProfileCollection> LoadAsync()
    {
        if (!File.Exists(_configFilePath))
        {
            // Return new empty collection if file doesn't exist
            return new ProfileCollection();
        }

        try
        {
            using var stream = File.OpenRead(_configFilePath);
            var collection = await JsonSerializer.DeserializeAsync<ProfileCollection>(stream, _jsonOptions);
            return collection ?? new ProfileCollection();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading profiles: {ex.Message}");
            // Return empty collection on error
            return new ProfileCollection();
        }
    }

    /// <summary>
    /// Saves the profile collection to disk with atomic write and backup.
    /// </summary>
    public async Task SaveAsync(ProfileCollection collection)
    {
        try
        {
            // Create backup if file exists
            if (File.Exists(_configFilePath))
            {
                var backupPath = $"{_configFilePath}.backup";
                File.Copy(_configFilePath, backupPath, overwrite: true);
            }

            // Write to temp file first (atomic write)
            var tempPath = $"{_configFilePath}.tmp";
            using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, collection, _jsonOptions);
            }

            // Atomically replace the config file
            File.Move(tempPath, _configFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving profiles: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Gets the path to the configuration directory.
    /// </summary>
    public string GetConfigDirectory() => _configDirectory;

    /// <summary>
    /// Gets the path to the configuration file.
    /// </summary>
    public string GetConfigFilePath() => _configFilePath;
}
