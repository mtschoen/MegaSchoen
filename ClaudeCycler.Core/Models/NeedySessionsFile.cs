using System.Text.Json.Serialization;

namespace ClaudeCycler.Core.Models;

public sealed class NeedySessionsFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("sessions")]
    public Dictionary<string, SessionEntry> Sessions { get; set; } = new();
}
