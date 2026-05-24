namespace Claude.Core.Models;

// ProcessId is the claude.exe CLI process; Window/Title belong to the parent
// terminal hosting it. StartTimeUtc is the claude.exe process start time and
// is used to disambiguate JSONLs when multiple sessions share a cwd.
public readonly record struct ClaudeWindow(
    uint ProcessId,
    WindowToken Window,
    string Title,
    string? WorkingDirectory,
    DateTimeOffset StartTimeUtc = default);
