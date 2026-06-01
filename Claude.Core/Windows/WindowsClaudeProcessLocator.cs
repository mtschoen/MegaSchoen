using Claude.Core.Models;

namespace Claude.Core.Windows;

public sealed class WindowsClaudeProcessLocator : IClaudeProcessLocator
{
    public IReadOnlyList<ClaudeWindow> EnumerateLiveSessions()
    {
        // Foreground sessions: shell-parented claude.exe, possibly with a window.
        var processes = ProcessResolver.EnumerateClaudeCliProcesses();
        var terminalsByCmdPid = ProcessResolver.GetTerminalWindowsByCmdPid();
        var result = new List<ClaudeWindow>(processes.Count);
        foreach (var process in processes)
        {
            // Include the process even when its parent shell has no visible
            // terminal window — it is still a live session (liveness gate),
            // it simply can't be focused (Window stays Null).
            var hasTerminal = terminalsByCmdPid.TryGetValue(process.ParentPid, out var terminal);
            result.Add(new ClaudeWindow(
                ProcessId: process.Pid,
                Window: hasTerminal ? WindowToken.FromHandle(terminal.WindowHandle) : WindowToken.Null,
                Title: hasTerminal ? terminal.WindowTitle : string.Empty,
                WorkingDirectory: process.WorkingDirectory,
                StartTimeUtc: process.StartTimeUtc));
        }

        // Background/daemon workers ("claude agents" / /bg): windowless, but they
        // carry their authoritative --session-id. Disjoint from the foreground
        // set (those are shell-parented; these are daemon-parented), so no dedup.
        var backgroundProcesses = ProcessResolver.EnumerateBackgroundClaudeSessions(out var sessionIdByPid);
        foreach (var process in backgroundProcesses)
        {
            result.Add(new ClaudeWindow(
                ProcessId: process.Pid,
                Window: WindowToken.Null,
                Title: string.Empty,
                WorkingDirectory: process.WorkingDirectory,
                StartTimeUtc: process.StartTimeUtc,
                SessionId: sessionIdByPid[process.Pid]));
        }
        return result;
    }
}
