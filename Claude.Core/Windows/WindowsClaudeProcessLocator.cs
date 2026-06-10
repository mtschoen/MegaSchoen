using Claude.Core.Models;

namespace Claude.Core.Windows;

public sealed class WindowsClaudeProcessLocator : IClaudeProcessLocator
{
    public IReadOnlyList<ClaudeWindow> EnumerateLiveSessions()
    {
        // Foreground sessions: shell-parented claude.exe, possibly with a window.
        var processes = ProcessResolver.EnumerateClaudeCliProcesses();
        var terminalsByCmdPid = ProcessResolver.GetTerminalWindowsByCmdPid();
        var visibleWindowsByPid = ProcessResolver.GetVisibleTopLevelWindowsByPid();
        var explorerPids = ProcessResolver.GetExplorerPids();
        var result = new List<ClaudeWindow>(processes.Count);
        foreach (var process in processes)
        {
            var window = WindowToken.Null;
            var title = string.Empty;
            if (terminalsByCmdPid.TryGetValue(process.ParentPid, out var terminal))
            {
                // Standalone terminal (cmd/pwsh window) - direct owner match.
                window = WindowToken.FromHandle(terminal.WindowHandle);
                title = terminal.WindowTitle;
            }
            else if (AncestorWindowResolver.Resolve(
                         startPid: process.ParentPid,
                         getParent: ProcessResolver.TryGetParentPid,
                         windowsByPid: visibleWindowsByPid,
                         stopPids: explorerPids,
                         maxDepth: 8) is { } hit)
            {
                // Embedded terminal (Rider/VS Code/devenv): the shell is windowless
                // inside the IDE; the IDE's main window is the focus target. Both
                // terminal tabs in one IDE share this window (cannot select a tab).
                window = WindowToken.FromHandle(hit.Hwnd);
                title = hit.Title;
            }
            // Else: still a live session (liveness gate); just not focusable.
            result.Add(new ClaudeWindow(
                ProcessId: process.Pid,
                Window: window,
                Title: title,
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
