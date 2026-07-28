using Claude.Core;
#if WINDOWS
using Claude.Core.Windows;
#else
using Claude.Core.Linux;
#endif

namespace AgentSessionsCLI.Commands;

static class FocusCommand
{
    public static Task<int> Run(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            Console.Error.WriteLine("focus: missing <session-id-prefix>");
            return Task.FromResult(1);
        }

        var prefix = arguments[0];
#if WINDOWS
        var locator = new WindowsClaudeProcessLocator();
        var focuser = new WindowsClaudeWindowFocuser();
#else
        var locator = new LinuxClaudeProcessLocator();
        var focuser = new LinuxClaudeWindowFocuser();
#endif
        var store = new StateStore();
        var enumerator = new ActiveSessionEnumerator([new ClaudeSessionSource(locator, store)]);
        var snapshots = enumerator.Enumerate();

        var matches = snapshots.Where(s => s.SessionId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"focus: no session matches prefix '{prefix}'");
            return Task.FromResult(1);
        }
        if (matches.Count > 1)
        {
            Console.Error.WriteLine($"focus: ambiguous prefix '{prefix}' matches {matches.Count} sessions");
            foreach (var m in matches) Console.Error.WriteLine($"  {m.SessionId}  {m.Cwd}");
            return Task.FromResult(1);
        }

        if (matches[0].Window.IsZero)
        {
            Console.Error.WriteLine($"focus: no terminal window attached for session '{matches[0].SessionId}' (headless or unresolved)");
            return Task.FromResult(2);
        }

        var ok = focuser.BringToFront(matches[0].Window);
        return Task.FromResult(ok ? 0 : 2);
    }
}
