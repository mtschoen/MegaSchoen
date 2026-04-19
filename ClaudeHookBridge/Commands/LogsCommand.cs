using ClaudeCycler.Core;

namespace ClaudeHookBridge.Commands;

public static class LogsCommand
{
    public static int Run()
    {
        if (!File.Exists(Paths.HookBridgeLog))
        {
            Console.WriteLine($"(no log file at {Paths.HookBridgeLog})");
            return 0;
        }

        using var stream = new FileStream(Paths.HookBridgeLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        Console.Write(reader.ReadToEnd());
        return 0;
    }
}
