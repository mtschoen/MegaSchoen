using System.Text.Json.Serialization;

namespace ClaudeCycler.Core.Models;

public sealed class SessionEntry
{
    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = "";

    [JsonPropertyName("notifiedAt")]
    public DateTimeOffset NotifiedAt { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
