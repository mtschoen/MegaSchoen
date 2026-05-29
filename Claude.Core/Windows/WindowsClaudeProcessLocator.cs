using Claude.Core.Models;

namespace Claude.Core.Windows;

public sealed class WindowsClaudeProcessLocator : IClaudeProcessLocator
{
    public IReadOnlyList<ClaudeWindow> EnumerateLiveSessions()
    {
        var processes = ProcessResolver.EnumerateClaudeCliProcesses();
        if (processes.Count == 0) return Array.Empty<ClaudeWindow>();

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
        return result;
    }
}
