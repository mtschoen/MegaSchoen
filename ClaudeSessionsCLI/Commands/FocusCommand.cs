using Claude.Core;
#if WINDOWS
using Claude.Core.Windows;
#endif

namespace ClaudeSessionsCLI.Commands;

static class FocusCommand
{
    public static Task<int> Run(string[] arguments)
    {
#if WINDOWS
        if (arguments.Length == 0)
        {
            Console.Error.WriteLine("focus: missing <session-id-prefix>");
            return Task.FromResult(1);
        }

        var prefix = arguments[0];
        var locator = new WindowsClaudeProcessLocator();
        var store = new StateStore();
        var enumerator = new ActiveSessionEnumerator(locator, store);
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

        var focuser = new WindowsClaudeWindowFocuser();
        var ok = focuser.BringToFront(matches[0].Window);
        return Task.FromResult(ok ? 0 : 2);
#else
        Console.Error.WriteLine("focus: not supported on this platform (Windows only)");
        return Task.FromResult(1);
#endif
    }
}
