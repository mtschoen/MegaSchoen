using Claude.Core;

namespace ClaudeHookBridge.Commands;

public static class InstallCommand
{
    public static int Run()
    {
        var selfPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(selfPath))
        {
            Console.Error.WriteLine("install: could not resolve own executable path");
            return 1;
        }
        new SettingsJsonInstaller().Install(selfPath);
        Console.WriteLine($"install: hooks pointing at {selfPath} written to {Paths.ClaudeSettingsFile}");
        return 0;
    }
}
