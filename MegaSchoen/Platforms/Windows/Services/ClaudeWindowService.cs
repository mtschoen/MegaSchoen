using Claude.Core;
using Claude.Core.Models;

namespace MegaSchoen.Platforms.Windows.Services;

sealed class ClaudeWindowService
{
    readonly TrayIconService _tray;
    readonly IClaudeWindowFocuser _focuser;
    readonly StateStore _store = new();
    readonly SessionLivenessVerifier _verifier = new();
    IntPtr _lastFocused = IntPtr.Zero;

    public ClaudeWindowService(TrayIconService tray, IClaudeWindowFocuser focuser)
    {
        _tray = tray;
        _focuser = focuser;
    }

    public void CycleToNext(WaitingReason? filter = null)
    {
        var entries = _store.Read();
        if (entries.Count == 0)
        {
            _tray.ShowNotification("MegaSchoen", "No Claude windows waiting", NotificationIcon.Info);
            return;
        }

        var windows = ProcessResolver.EnumerateCmdExeWindows();
        var candidates = new List<(string SessionId, CmdWindow Window, DateTimeOffset NotifiedAt)>();
        var matchedSessionIds = new HashSet<string>();
        foreach (var (id, entry) in entries)
        {
            if (!_verifier.IsStillWaiting(entry))
            {
                _store.Delete(id);
                continue;
            }

            var includeInCycle = filter is null || entry.Reason == filter;

            foreach (var window in windows)
            {
                if (CwdMatches(window.WorkingDirectory, entry.Cwd))
                {
                    matchedSessionIds.Add(id);
                    if (includeInCycle)
                    {
                        candidates.Add((id, window, entry.NotifiedAt));
                    }
                }
            }
        }

        foreach (var id in entries.Keys)
        {
            if (!matchedSessionIds.Contains(id))
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

        _focuser.BringToFront(WindowToken.FromHandle(next.Window.WindowHandle));
        _lastFocused = next.Window.WindowHandle;
    }

    static bool CwdMatches(string? windowCwd, string sessionCwd) =>
        windowCwd is not null
        && string.Equals(
            windowCwd.TrimEnd('\\'),
            sessionCwd.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
}
