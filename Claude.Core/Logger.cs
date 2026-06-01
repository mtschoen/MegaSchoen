namespace Claude.Core;

public static class Logger
{
    static readonly object Lock = new();

    public static void Log(string message)
    {
        try
        {
            Paths.EnsureAppDataDirectoryExists();
            var line = $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}";
            lock (Lock)
            {
                File.AppendAllText(Paths.HookBridgeLog, line);
            }
        }
        catch
        {
            /* Never throw from logging: this runs inside hook handlers and the
               logger is the last resort, so there is nowhere else to report to. */
        }
    }
}
