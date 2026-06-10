namespace Claude.Core;

// Pure orchestration: localPort -> owning pid -> verify ssh.exe -> ancestor
// window. Every external effect is an injected delegate so this unit-tests with
// no Win32. The production wiring (Task 11) passes:
//   portToPid   = TcpConnectionTable.TryGetOwningPidForLocalPort
//   processName = a Process.GetProcessById(pid).ProcessName lookup
//   ancestor    = pid => AncestorWindowResolver.Resolve(pid, TryGetParentPid,
//                          GetVisibleTopLevelWindowsByPid(), GetExplorerPids(), 8)
public sealed class SshSessionWindowResolver
{
    readonly Func<int, uint?> _portToPid;
    readonly Func<uint, string?> _processName;
    readonly Func<uint, AncestorWindowResolver.WindowHit?> _ancestorWindow;

    public SshSessionWindowResolver(
        Func<int, uint?> portToPid,
        Func<uint, string?> processName,
        Func<uint, AncestorWindowResolver.WindowHit?> ancestorWindow)
    {
        _portToPid = portToPid;
        _processName = processName;
        _ancestorWindow = ancestorWindow;
    }

    public AncestorWindowResolver.WindowHit? Resolve(int sshClientPort)
    {
        if (sshClientPort <= 0) return null;
        if (_portToPid(sshClientPort) is not { } pid) return null;
        // Reject anything that is not ssh.exe (stale port reuse, wrong match).
        var name = _processName(pid);
        if (!string.Equals(name, "ssh", StringComparison.OrdinalIgnoreCase)) return null;
        return _ancestorWindow(pid);
    }
}
