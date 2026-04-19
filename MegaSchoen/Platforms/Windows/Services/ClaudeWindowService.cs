using ClaudeCycler.Core;

namespace MegaSchoen.Platforms.Windows.Services;

sealed class ClaudeWindowService
{
    readonly TrayIconService _tray;
    readonly StateStore _store = new();
    IntPtr _lastFocused = IntPtr.Zero;

    public ClaudeWindowService(TrayIconService tray)
    {
        _tray = tray;
    }

    public void CycleToNext()
    {
        Logger.Log("CycleToNext: start");
        var file = _store.Read();
        Logger.Log($"CycleToNext: state has {file.Sessions.Count} session(s)");
        if (file.Sessions.Count == 0)
        {
            _tray.ShowNotification("MegaSchoen", "No Claude windows waiting", NotificationIcon.Info);
            return;
        }

        var windows = ProcessResolver.EnumerateCmdExeWindows();
        Logger.Log($"CycleToNext: enumerated {windows.Count} cmd.exe window(s)");
        var candidates = new List<(string SessionId, CmdWindow Window, DateTimeOffset NotifiedAt)>();
        var matchedSessionIds = new HashSet<string>();
        foreach (var (id, entry) in file.Sessions)
        {
            foreach (var window in windows)
            {
                if (CwdMatches(window.WorkingDirectory, entry.Cwd))
                {
                    candidates.Add((id, window, entry.NotifiedAt));
                    matchedSessionIds.Add(id);
                }
            }
        }
        Logger.Log($"CycleToNext: built {candidates.Count} candidate(s) after cwd match");

        foreach (var id in file.Sessions.Keys)
        {
            if (!matchedSessionIds.Contains(id))
            {
                Logger.Log($"CycleToNext: pruning zombie session {id}");
                _store.Delete(id);
            }
        }

        if (candidates.Count == 0)
        {
            _tray.ShowNotification("MegaSchoen", "No live Claude windows waiting", NotificationIcon.Info);
            return;
        }

        candidates.Sort((a, b) => a.NotifiedAt.CompareTo(b.NotifiedAt));

        var lastIndex = candidates.FindIndex(c => c.Window.WindowHandle == _lastFocused);
        var nextIndex = (lastIndex + 1) % candidates.Count;
        var next = candidates[nextIndex];
        Logger.Log($"CycleToNext: picked index {nextIndex} of {candidates.Count}: pid={next.Window.ProcessId} hwnd=0x{next.Window.WindowHandle:X}");

        var brought = Win32ForegroundHelper.BringToFront(next.Window.WindowHandle);
        Logger.Log($"CycleToNext: SetForegroundWindow returned {brought}");
        _lastFocused = next.Window.WindowHandle;
    }

    static bool CwdMatches(string? windowCwd, string sessionCwd) =>
        windowCwd is not null
        && string.Equals(
            windowCwd.TrimEnd('\\'),
            sessionCwd.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
}
