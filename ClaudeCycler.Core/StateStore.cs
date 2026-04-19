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
}
