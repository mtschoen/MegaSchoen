namespace Claude.Core;

// Pure, platform-neutral: walk a process-ancestor chain and return the first
// ancestor PID that owns a visible top-level window. Win32 is injected as
// delegates (getParent) + a prebuilt window map, so this is fully unit-testable.
// Used by BOTH exotic-context features:
//   - embedded IDE terminals: start at the windowless shell PID -> IDE window
//   - remote ssh: start at the local ssh.exe PID -> hosting cmd window
public static class AncestorWindowResolver
{
    public readonly record struct WindowHit(IntPtr Hwnd, string Title);

    // startPid is included in the walk (a direct window on the start process is
    // a valid hit). stopPids halt the climb WITHOUT matching (e.g. explorer.exe,
    // so a standalone-terminal chain never resolves to a File Explorer/desktop
    // window). Cycle-guarded and depth-bounded.
    public static WindowHit? Resolve(
        uint startPid,
        Func<uint, uint?> getParent,
        IReadOnlyDictionary<uint, (IntPtr Hwnd, string Title)> windowsByPid,
        IReadOnlySet<uint> stopPids,
        int maxDepth)
    {
        var seen = new HashSet<uint>();
        var current = startPid;
        for (var depth = 0; depth <= maxDepth; depth++)
        {
            if (!seen.Add(current)) return null;     // cycle guard
            if (stopPids.Contains(current)) return null;
            if (windowsByPid.TryGetValue(current, out var win))
            {
                return new WindowHit(win.Hwnd, win.Title);
            }
            if (getParent(current) is not { } parent || parent == current || parent == 0) return null;
            current = parent;
        }
        return null;
    }
}
