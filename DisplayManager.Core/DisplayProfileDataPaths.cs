namespace DisplayManager.Core;

static class DisplayProfileDataPaths
{
    public const string OverrideEnvironmentVariable = "MEGASCHOEN_PROFILE_DATA_DIRECTORY";

    public static string ConfigurationDirectory =>
        ResolveDirectory(
            Environment.GetEnvironmentVariable(OverrideEnvironmentVariable),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public static string LocalStateDirectory =>
        ResolveDirectory(
            Environment.GetEnvironmentVariable(OverrideEnvironmentVariable),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static string ResolveDirectory(string? overrideDirectory, string platformDirectory) =>
        string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(platformDirectory, "MegaSchoen")
            : Path.GetFullPath(overrideDirectory);
}
