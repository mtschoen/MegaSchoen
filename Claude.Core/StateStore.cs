using System.Text.Json;
using System.Threading;
using Claude.Core.Models;

namespace Claude.Core;

// State is sharded one-file-per-session under NeedySessionsDirectory so that
// concurrent ClaudeHookBridge.exe invocations from different Claude sessions
// never contend on the same file. Within a single session, hook events are
// expected to be serial, but a per-session named mutex guards against the
// rare case where Claude fires overlapping hooks (or where the bridge writer
// for one session races a periodic sweep that's deleting the same file).
public sealed class StateStore
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public StateStore() : this(Paths.NeedySessionsDirectory) { }

    public StateStore(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    public IReadOnlyDictionary<string, SessionEntry> Read()
    {
        var result = new Dictionary<string, SessionEntry>();
        if (!System.IO.Directory.Exists(Directory))
        {
            return result;
        }

        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            var sessionId = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(sessionId)) continue;

            try
            {
                var text = File.ReadAllText(path);
                var entry = JsonSerializer.Deserialize<SessionEntry>(text);
                if (entry is not null)
                {
                    result[sessionId] = entry;
                }
            }
            catch (Exception exception)
            {
                Logger.Log($"StateStore.Read failed for {path}: {exception.Message}");
            }
        }
        return result;
    }

    public IReadOnlyCollection<string> EnumerateSessionIds()
    {
        if (!System.IO.Directory.Exists(Directory)) return Array.Empty<string>();
        return System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .Cast<string>()
            .ToArray();
    }

    public SessionEntry? ReadSession(string sessionId)
    {
        var path = GetPath(sessionId);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<SessionEntry>(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            Logger.Log($"StateStore.ReadSession failed for {sessionId}: {exception.Message}");
            return null;
        }
    }

    public void Upsert(string sessionId, SessionEntry entry)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = GetPath(sessionId);
        var tempPath = path + ".tmp";
        var serialized = JsonSerializer.Serialize(entry, JsonOptions);

        WithSessionMutex(sessionId, () =>
        {
            File.WriteAllText(tempPath, serialized);
            File.Move(tempPath, path, overwrite: true);
        });
    }

    public void Delete(string sessionId)
    {
        var path = GetPath(sessionId);
        WithSessionMutex(sessionId, () =>
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        });
    }

    public void DeleteAll()
    {
        if (!System.IO.Directory.Exists(Directory)) return;
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            var sessionId = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(sessionId)) continue;
            try
            {
                Delete(sessionId);
            }
            catch (Exception exception)
            {
                Logger.Log($"StateStore.DeleteAll failed for {sessionId}: {exception.Message}");
            }
        }
    }

    string GetPath(string sessionId) => Paths.GetSessionFilePath(sessionId, Directory);

    static void WithSessionMutex(string sessionId, Action action)
    {
        // Local\ scope is per-Windows-session, which matches our per-user state.
        // Mutex names are limited to 260 chars and forbid backslashes in the body;
        // session IDs are GUIDs so they are always safe.
        var mutexName = $"Local\\MegaSchoen.Session.{sessionId}";
        using var mutex = new Mutex(initiallyOwned: false, name: mutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                acquired = true; // the previous holder crashed — we now own it
            }

            if (!acquired)
            {
                Logger.Log($"StateStore: mutex timeout for {sessionId}; proceeding without lock");
            }
            action();
        }
        finally
        {
            if (acquired)
            {
                try { mutex.ReleaseMutex(); } catch (ApplicationException) { /* not held */ }
            }
        }
    }
}
