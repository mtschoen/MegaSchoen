using Claude.Core.Models;

namespace Claude.Core;

// Platform-neutral no-op window services for hosts with no window integration
// yet (Linux/macOS). Sessions enumerate normally; Focus simply never resolves,
// so the UI renders those sessions with the Focus affordance hidden/disabled.

public sealed class NullClaudeWindowFocuser : IClaudeWindowFocuser
{
    public bool BringToFront(WindowToken window) => false;
}

public sealed class NullSshSessionWindowResolver : ISshSessionWindowResolver
{
    public (WindowToken Window, string Title)? ResolveWindow(int sshClientPort) => null;
}
