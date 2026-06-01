namespace Claude.Core;

public static class Paths
{
    public static string AppDataDirectory { get; } =
        Path.Combine(
            Environment.GetEnvironmentVariable("LOCALAPPDATA")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MegaSchoen");

    public static string NeedySessionsDirectory { get; } =
        Path.Combine(AppDataDirectory, "needy-sessions");

    // Pre-2026-05-23 single-file state store. Retained only so startup migration
    // can delete the leftover blob; nothing should read from this path anymore.
    public static string LegacyNeedySessionsFile { get; } =
        Path.Combine(AppDataDirectory, "needy-sessions.json");

    public static string HookBridgeLog { get; } =
        Path.Combine(AppDataDirectory, "hook-bridge.log");

    // Default destination for the diagnostic hook-payload capture tee
    // (see HookCapture). Only written when MEGASCHOEN_HOOK_CAPTURE is set.
    public static string HookCaptureLog { get; } =
        Path.Combine(AppDataDirectory, "hook-capture.ndjson");

    public static string ClaudeSettingsFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static void EnsureAppDataDirectoryExists() =>
        Directory.CreateDirectory(AppDataDirectory);

    public static void EnsureNeedySessionsDirectoryExists() =>
        Directory.CreateDirectory(NeedySessionsDirectory);

    public static string GetSessionFilePath(string sessionId, string? directoryOverride = null) =>
        Path.Combine(directoryOverride ?? NeedySessionsDirectory, $"{sessionId}.json");
}
