using System.Text.Json;
using System.Text.Json.Serialization;
using Claude.Core.Models;

namespace Claude.Core;

// Test/replay-only IClaudeProcessLocator selected when MEGASCHOEN_FAKE_PROCESSES
// is set (JSON: [{ "cwd": "...", "count": N }]). Emits windowless live sessions
// so the replay harness satisfies the per-cwd liveness gate with no real
// claude.exe. No-op (empty) when the variable is unset — never active in normal
// use. This is a documented test affordance, not a production code path.
public sealed class EnvironmentProcessLocator : IClaudeProcessLocator
{
    public const string EnvironmentVariable = "MEGASCHOEN_FAKE_PROCESSES";

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    readonly IReadOnlyList<ClaudeWindow> _sessions;

    public EnvironmentProcessLocator()
        => _sessions = Parse(Environment.GetEnvironmentVariable(EnvironmentVariable));

    public IReadOnlyList<ClaudeWindow> EnumerateLiveSessions() => _sessions;

    public static IReadOnlyList<ClaudeWindow> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<ClaudeWindow>();

        var specs = JsonSerializer.Deserialize<List<FakeProcSpec>>(json, Options)
            ?? new List<FakeProcSpec>();
        var result = new List<ClaudeWindow>();
        uint pid = 1;
        foreach (var spec in specs)
        {
            for (var i = 0; i < spec.Count; i++)
            {
                result.Add(new ClaudeWindow(
                    pid++, WindowToken.Null, "", spec.Cwd, DateTimeOffset.UtcNow));
            }
        }
        return result;
    }

    sealed class FakeProcSpec
    {
        [JsonPropertyName("cwd")] public string Cwd { get; set; } = "";
        [JsonPropertyName("count")] public int Count { get; set; }
    }
}
