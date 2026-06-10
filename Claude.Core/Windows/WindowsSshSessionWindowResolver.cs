using System.Diagnostics;
using Claude.Core.Models;

namespace Claude.Core.Windows;

// Production ISshSessionWindowResolver: wires the pure SshSessionWindowResolver
// to real Win32 (TCP table, process name, ancestor window map). Rebuilds the
// visible-window map per call so it reflects current window state.
public sealed class WindowsSshSessionWindowResolver : ISshSessionWindowResolver
{
    public (WindowToken Window, string Title)? ResolveWindow(int sshClientPort)
    {
        var windowsByPid = ProcessResolver.GetVisibleTopLevelWindowsByPid();
        // A cmd/pwsh hosting ssh owns only a ConPTY PseudoConsoleWindow (or a
        // classic console), so its visible terminal is keyed by the SHELL pid in
        // the terminal map, not by the root-owner pid that GetVisibleTopLevel
        // WindowsByPid records. Merge it in so the ancestor walk (ssh -> cmd)
        // resolves the hosting terminal window instead of dead-ending at cmd.
        // TryAdd: the terminal map only fills gaps; a window the shell pid
        // directly owns (already in the map) takes precedence.
        foreach (var (shellPid, terminal) in ProcessResolver.GetTerminalWindowsByCmdPid())
        {
            windowsByPid.TryAdd(shellPid, (terminal.WindowHandle, terminal.WindowTitle));
        }
        var explorerPids = ProcessResolver.GetExplorerPids();

        var resolver = new SshSessionWindowResolver(
            portToPid: TcpConnectionTable.TryGetOwningPidForLocalPort,
            processName: TryGetProcessName,
            ancestorWindow: pid => AncestorWindowResolver.Resolve(
                pid, ProcessResolver.TryGetParentPid, windowsByPid, explorerPids, maxDepth: 8));

        return resolver.Resolve(sshClientPort) is { } hit
            ? (WindowToken.FromHandle(hit.Hwnd), hit.Title)
            : null;
    }

    static string? TryGetProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;   // "ssh" for ssh.exe
        }
        catch { return null; }
    }
}
