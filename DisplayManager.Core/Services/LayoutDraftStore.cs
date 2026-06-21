using System.Text.Json;
using DisplayManager.Core.Models;

namespace DisplayManager.Core.Services;

/// <summary>
/// Persists per-preset layout drafts as one JSON file each under
/// %APPDATA%\MegaSchoen\layout-drafts\&lt;presetId&gt;.json. Separate from configs.json
/// so a stashed (possibly invalid) draft never touches a real preset.
/// </summary>
public class LayoutDraftStore
{
    readonly string _directory;
    readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public LayoutDraftStore() : this(DefaultDirectory()) { }

    public LayoutDraftStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    static string DefaultDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "MegaSchoen", "layout-drafts");
    }

    string PathFor(Guid presetId) => Path.Combine(_directory, $"{presetId:N}.json");

    public async Task SaveAsync(LayoutDraft draft)
    {
        draft.LastModified = DateTime.UtcNow;
        var tempPath = PathFor(draft.PresetId) + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, draft, _jsonOptions);
        }
        File.Move(tempPath, PathFor(draft.PresetId), overwrite: true);
    }

    public async Task<LayoutDraft?> LoadAsync(Guid presetId)
    {
        var path = PathFor(presetId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<LayoutDraft>(stream, _jsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            // Corrupt draft or a file-access race (deleted/locked between the
            // Exists check and the open): treat as no draft. Other failures
            // propagate so they stay diagnosable.
            DiagnosticLog.Log($"LayoutDraftStore.LoadAsync({presetId}): {exception.Message}");
            return null;
        }
    }

    public Task DeleteAsync(Guid presetId)
    {
        var path = PathFor(presetId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }
}
