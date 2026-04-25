using ClaudeCycler.Core;

namespace MegaSchoen.Platforms.Windows.Services;

sealed class ClaudeWindowService
{
    readonly TrayIconService _tray;
    readonly StateStore _store = new();
    readonly SessionLivenessVerifier _verifier = new();
    IntPtr _lastFocused = IntPtr.Zero;

    public ClaudeWindowService(TrayIconService tray)
    {
        _tray = tray;
    }

    public void CycleToNext()
    {
        var file = _store.Read();
        if (file.Sessions.Count == 0)
        {
            _tray.ShowNotification("MegaSchoen", "No Claude windows waiting", NotificationIcon.Info);
            return;
        }

        var windows = ProcessResolver.EnumerateCmdExeWindows();
        var candidates = new List<(string SessionId, CmdWindow Window, DateTimeOffset NotifiedAt)>();
        var matchedSessionIds = new HashSet<string>();
        foreach (var (id, entry) in file.Sessions)
        {
            if (!_verifier.IsStillWaiting(entry))
            {
                _store.Delete(id);
                continue;
            }

            foreach (var window in windows)
            {
                if (CwdMatches(window.WorkingDirectory, entry.Cwd))
                {
                    candidates.Add((id, window, entry.NotifiedAt));
                    matchedSessionIds.Add(id);
                }
            }
        }

        foreach (var id in file.Sessions.Keys)
        {
            if (!matchedSessionIds.Contains(id) && file.Sessions.ContainsKey(id))
            {
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

        Win32ForegroundHelper.BringToFront(next.Window.WindowHandle);
        _lastFocused = next.Window.WindowHandle;
    }

    static bool CwdMatches(string? windowCwd, string sessionCwd) =>
        windowCwd is not null
        && string.Equals(
            windowCwd.TrimEnd('\\'),
            sessionCwd.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
}
