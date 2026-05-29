using Claude.Core.Models;

namespace Claude.Core;

public interface IClaudeProcessLocator
{
    // Returns every live top-level Claude CLI session as a ClaudeWindow.
    // NOTE: an entry's Window may be WindowToken.Null when the session has no
    // visible terminal (headless `claude -p`, or a shell with no window). Such
    // entries still count for liveness; they just can't be focused.
    IReadOnlyList<ClaudeWindow> EnumerateLiveSessions();
}
