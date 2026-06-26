using Claude.Core.Models;

namespace Claude.Core;

// Maps a session's status to a glyph for at-a-glance display (the sessions CLI
// today; reusable by the MAUI app or any status surface later). Every
// SessionState is mapped explicitly so adding a state is caught by the
// exhaustiveness test rather than silently falling through to a placeholder.
public static class SessionStateEmoji
{
    public static string For(SessionState state) => state switch
    {
        SessionState.PendingPermission => "🙋",
        SessionState.AwaitingInput => "⌨️",
        SessionState.Working => "🔄",
        SessionState.Idle => "😴",
        SessionState.Unknown => "❓",
        _ => "❓"
    };
}
