using System.Text.Json;
using ClaudeCycler.Core.Models;

namespace ClaudeCycler.Core;

public sealed class StateStore
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    readonly string _path;
    readonly object _lock = new();

    public StateStore(string path)
    {
        _path = path;
    }

    public StateStore() : this(Paths.NeedySessionsFile) { }

    public NeedySessionsFile Read()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return new NeedySessionsFile();
            }

            try
            {
                var text = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<NeedySessionsFile>(text) ?? new NeedySessionsFile();
            }
            catch (Exception exception)
            {
                Logger.Log($"StateStore.Read failed: {exception.Message}");
                return new NeedySessionsFile();
            }
        }
    }

    public void Write(NeedySessionsFile file)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(file, JsonOptions));
            File.Move(tempPath, _path, overwrite: true);
        }
    }

    public void Upsert(string sessionId, SessionEntry entry)
    {
        var file = Read();
        file.Sessions[sessionId] = entry;
        Write(file);
    }

    public void Delete(string sessionId)
    {
        var file = Read();
        if (file.Sessions.Remove(sessionId))
        {
            Write(file);
        }
    }
}
