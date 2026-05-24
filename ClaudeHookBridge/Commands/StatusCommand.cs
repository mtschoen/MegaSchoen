using Claude.Core;

namespace ClaudeHookBridge.Commands;

public static class StatusCommand
{
    public static int Run()
    {
        var store = new StateStore();
        var entries = store.Read();
        var now = DateTimeOffset.UtcNow;

        Console.WriteLine($"State directory: {Paths.NeedySessionsDirectory}");
        Console.WriteLine($"Sessions: {entries.Count}");
        Console.WriteLine();

        foreach (var (id, entry) in entries)
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
