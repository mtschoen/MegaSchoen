using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claude.Core.Tests.Fixtures;

public sealed class SessionScenario
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("cwd")] public string Cwd { get; set; } = "";
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = "";
    [JsonPropertyName("steps")] public List<ScenarioStep> Steps { get; set; } = new();

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static SessionScenario Load(string path) =>
        JsonSerializer.Deserialize<SessionScenario>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException($"Could not parse scenario: {path}");

    public static IEnumerable<string> FixtureFiles(string fixturesDir) =>
        Directory.EnumerateFiles(fixturesDir, "*.json").OrderBy(p => p);
}

public sealed class ScenarioStep
{
    [JsonPropertyName("event")] public string Event { get; set; } = "";
    [JsonPropertyName("notificationType")] public string? NotificationType { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("delayMs")] public int DelayMs { get; set; }
    [JsonPropertyName("expectAfter")] public string ExpectAfter { get; set; } = "";
}
