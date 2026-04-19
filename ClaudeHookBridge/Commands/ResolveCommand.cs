using ClaudeCycler.Core;

namespace ClaudeHookBridge.Commands;

public static class ResolveCommand
{
    public static int Run()
    {
        var store = new StateStore();
        var file = store.Read();

        Console.WriteLine($"Sessions: {file.Sessions.Count}");
        var windows = ProcessResolver.EnumerateCmdExeWindows();
        Console.WriteLine($"cmd.exe windows: {windows.Count}");
        Console.WriteLine();

        foreach (var (id, entry) in file.Sessions)
        {
            Console.WriteLine($"Session {id}");
            Console.WriteLine($"  cwd: {entry.Cwd}");

            var matches = windows.Where(w => CwdMatches(w.WorkingDirectory, entry.Cwd)).ToList();
            if (matches.Count == 0)
            {
                Console.WriteLine("  -> NO MATCHING WINDOW");
            }
            else
            {
                foreach (var w in matches)
                {
                    Console.WriteLine($"  -> pid={w.ProcessId} hwnd=0x{w.WindowHandle:X} title=\"{w.WindowTitle}\"");
                }
            }
        }

        return 0;
    }

    static bool CwdMatches(string? windowCwd, string sessionCwd) =>
        windowCwd is not null
        && string.Equals(
            windowCwd.TrimEnd('\\'),
            sessionCwd.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
}
