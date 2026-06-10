using Claude.Core.Models;

namespace Claude.Core;

// Given a remote session's ssh client source port, resolve the local terminal
// WindowToken that hosts the interactive ssh.exe (or Null if it cannot be
// correlated). Windows-backed in production; delegate-injected in tests.
public interface ISshSessionWindowResolver
{
    (WindowToken Window, string Title)? ResolveWindow(int sshClientPort);
}
