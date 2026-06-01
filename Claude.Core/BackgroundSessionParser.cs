namespace Claude.Core;

// Pure parser for the background ("claude agents" / /bg) worker process shape:
//   claude.exe --session-id <GUID> --agent <name>
// spawned under  claude.exe daemon run  >  claude.exe --bg-pty-host ...
// The worker carries its real session_id on the command line - authoritative
// identity, no transcript correlation needed. The pty-host shares the GUID in
// its pipe name and re-spawns the worker after a `--`, so reject any command
// line that is itself a --bg-pty-host or the daemon (only the leaf worker is
// the session).
public static class BackgroundSessionParser
{
    public static bool TryParseWorkerSessionId(string? commandLine, out string sessionId)
    {
        sessionId = "";
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        if (commandLine.Contains("--bg-pty-host", StringComparison.Ordinal)) return false;
        if (commandLine.Contains("daemon run", StringComparison.Ordinal)) return false;

        const string flag = "--session-id";
        var index = commandLine.IndexOf(flag, StringComparison.Ordinal);
        if (index < 0) return false;

        var rest = commandLine[(index + flag.Length)..].TrimStart();
        var end = rest.IndexOfAny(new[] { ' ', '\t' });
        var token = (end < 0 ? rest : rest[..end]).Trim().Trim('"');
        if (!Guid.TryParse(token, out var guid)) return false;

        sessionId = guid.ToString("D");
        return true;
    }
}
