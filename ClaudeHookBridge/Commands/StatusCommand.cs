using ClaudeCycler.Core;

namespace ClaudeHookBridge.Commands;

public static class StatusCommand
{
    public static int Run()
    {
        var store = new StateStore();
        var file = store.Read();
        var now = DateTimeOffset.UtcNow;

        Console.WriteLine($"State file: {Paths.NeedySessionsFile}");
        Console.WriteLine($"Version: {file.Version}");
        Console.WriteLine($"Sessions: {file.Sessions.Count}");
        Console.WriteLine();

        foreach (var (id, entry) in file.Sessions)
        {
            var age = now - entry.NotifiedAt;
            Console.WriteLine($"  {id}");
            Console.WriteLine($"    cwd:        {entry.Cwd}");
            Console.WriteLine($"    notifiedAt: {entry.NotifiedAt:O} ({age.TotalMinutes:F1} min ago)");
            Console.WriteLine($"    message:    {entry.Message ?? "(none)"}");
        }

        return 0;
    }
}
