using Claude.Core.Models;

namespace Claude.Core.Windows;

public sealed class WindowsClaudeProcessLocator : IClaudeProcessLocator
{
    public IReadOnlyList<ClaudeWindow> EnumerateWindows()
    {
        var processes = ProcessResolver.EnumerateClaudeCliProcesses();
        if (processes.Count == 0) return Array.Empty<ClaudeWindow>();

        var terminalsByCmdPid = ProcessResolver.GetTerminalWindowsByCmdPid();
        var result = new List<ClaudeWindow>(processes.Count);
        foreach (var process in processes)
        {
            if (!terminalsByCmdPid.TryGetValue(process.ParentPid, out var terminal)) continue;
            result.Add(new ClaudeWindow(
                ProcessId: process.Pid,
                Window: WindowToken.FromHandle(terminal.WindowHandle),
                Title: terminal.WindowTitle,
                WorkingDirectory: process.WorkingDirectory,
                StartTimeUtc: process.StartTimeUtc));
        }
        return result;
    }
}
