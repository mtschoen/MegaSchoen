namespace MegaSchoen.Platforms.Windows.Services;

/// <summary>
/// Manages Windows startup registration via shortcut in the Startup folder.
/// </summary>
static class StartupService
{
    const string ShortcutName = "MegaSchoen.lnk";

    static string StartupFolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        ShortcutName);

    /// <summary>
    /// Checks if the application is configured to start with Windows.
    /// </summary>
    public static bool IsStartupEnabled => File.Exists(StartupFolderPath);

    /// <summary>
    /// Enables or disables startup with Windows.
    /// </summary>
    public static void SetStartupEnabled(bool enabled)
    {
        if (enabled)
        {
            CreateStartupShortcut();
        }
        else
        {
            RemoveStartupShortcut();
        }
    }

    static void CreateStartupShortcut()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            throw new InvalidOperationException("Could not determine application path");
        }

        // Use COM interop with WScript.Shell to create the shortcut
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            throw new InvalidOperationException("WScript.Shell is not available");
        }

        var shellInstance = Activator.CreateInstance(shellType);
        if (shellInstance is null)
        {
            throw new InvalidOperationException("Could not create a WScript.Shell instance");
        }
        dynamic shell = shellInstance;
        try
        {
            dynamic shortcut = shell.CreateShortcut(StartupFolderPath);
            try
            {
                shortcut.TargetPath = exePath;
                shortcut.Arguments = "--minimized";
                shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
                shortcut.Description = "MegaSchoen Display Manager";
                shortcut.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }

    static void RemoveStartupShortcut()
    {
        if (File.Exists(StartupFolderPath))
        {
            File.Delete(StartupFolderPath);
        }
    }
}
