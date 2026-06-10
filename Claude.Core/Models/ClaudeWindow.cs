namespace Claude.Core.Models;

// ProcessId is the claude.exe CLI process; Window/Title belong to the parent
// terminal hosting it. StartTimeUtc is the claude.exe process start time and
// is used to disambiguate JSONLs when multiple sessions share a cwd. SessionId
// is set ONLY for background/daemon workers that carry --session-id on their
// command line (authoritative identity); null for foreground/headless sessions
// whose identity is derived downstream from the transcript/cwd.
// SshClientPort is the remote ssh client source port (from SSH_CONNECTION) when
// the session runs inside an interactive ssh login; null otherwise (set only on
// the Linux/remote side, used locally to find the hosting ssh.exe window).
public readonly record struct ClaudeWindow(
    uint ProcessId,
    WindowToken Window,
    string Title,
    string? WorkingDirectory,
    DateTimeOffset StartTimeUtc = default,
    string? SessionId = null,
    int? SshClientPort = null);
