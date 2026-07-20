using System;
using System.IO;

namespace MegaSchoen.Avalonia;

// XDG autostart toggle: a .desktop entry under ~/.config/autostart launches
// the dashboard hidden to the tray on login - the Linux analog of the MAUI
// app's Windows startup registration. Enabled == the entry file exists.
static class AutostartService
{
    static string AutostartDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "autostart");

    static string EntryPath => Path.Combine(AutostartDirectory, "megaschoen-sessions.desktop");

    public static bool IsEnabled => File.Exists(EntryPath);

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(EntryPath);
            return;
        }

        Directory.CreateDirectory(AutostartDirectory);
        File.WriteAllText(EntryPath, $"""
            [Desktop Entry]
            Type=Application
            Name=MegaSchoen Sessions
            Comment=Active Claude sessions dashboard (starts hidden to tray)
            Exec={LauncherPath()} --hidden
            Icon=megaschoen
            Terminal=false

            """);
    }

    // Prefer the machine's launcher script (it sets DOTNET_ROOT, which login
    // autostart processes do not otherwise inherit); fall back to the running
    // apphost for setups without one.
    static string LauncherPath()
    {
        var launcher = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "bin", "megaschoen-sessions");
        return File.Exists(launcher) ? launcher : Environment.ProcessPath ?? launcher;
    }
}
