namespace Claude.Core.Models;

public sealed record SubagentSnapshot(
    string AgentId,
    string TranscriptPath,
    DateTimeOffset LastActivityUtc,
    SessionState State);
