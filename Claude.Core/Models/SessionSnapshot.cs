namespace Claude.Core.Models;

public sealed record SessionSnapshot(
    string SessionId,
    string Cwd,
    string TranscriptPath,
    DateTimeOffset LastActivityUtc,
    SessionState State,
    string? PendingMessage,
    WindowToken Window,
    string? WindowTitle,
    IReadOnlyList<SubagentSnapshot> Subagents,
    string? Host = null,
    int? SshClientPort = null,
    SessionMode Mode = SessionMode.Unknown,
    string? Title = null)
{
    public string CurrentCwd { get; init; } = Cwd;

    public SessionState RollupState
    {
        get
        {
            if (Subagents.Count == 0) return State;
            var minSubagent = Subagents.Min(s => (int)s.State);
            return (SessionState)Math.Min((int)State, minSubagent);
        }
    }
}
