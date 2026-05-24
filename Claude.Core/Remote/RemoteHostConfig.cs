using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claude.Core.Remote;

public sealed class RemoteHostConfig
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("sshTarget")] public string SshTarget { get; set; } = "";
    [JsonPropertyName("remoteCli")] public string RemoteCli { get; set; } = "claude-sessions";

    public static string DefaultPath =>
        Path.Combine(Paths.AppDataDirectory, "remote-hosts.json");

    public static IReadOnlyList<RemoteHostConfig> Load() => Load(DefaultPath);

    public static IReadOnlyList<RemoteHostConfig> Load(string path)
    {
        if (!File.Exists(path)) return Array.Empty<RemoteHostConfig>();
        try
        {
            var hosts = JsonSerializer.Deserialize<List<RemoteHostConfig>>(File.ReadAllText(path));
            return hosts?.Where(h => !string.IsNullOrWhiteSpace(h.SshTarget)).ToList()
                   ?? (IReadOnlyList<RemoteHostConfig>)Array.Empty<RemoteHostConfig>();
        }
        catch { return Array.Empty<RemoteHostConfig>(); }
    }
}
