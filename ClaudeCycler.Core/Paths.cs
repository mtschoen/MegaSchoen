namespace ClaudeCycler.Core;

public static class Paths
{
    public static string AppDataDirectory { get; } =
        Path.Combine(
            Environment.GetEnvironmentVariable("LOCALAPPDATA")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MegaSchoen");

    public static string NeedySessionsFile { get; } =
        Path.Combine(AppDataDirectory, "needy-sessions.json");

    public static string HookBridgeLog { get; } =
        Path.Combine(AppDataDirectory, "hook-bridge.log");

    public static string ClaudeSettingsFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static void EnsureAppDataDirectoryExists() =>
        Directory.CreateDirectory(AppDataDirectory);
}
