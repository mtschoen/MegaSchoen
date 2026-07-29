using System;
using System.IO;

namespace MegaSchoen.Avalonia;

static class AutostartService
{
    static readonly AutostartController Controller = CreateController();

    public static bool IsEnabled => Controller.IsEnabled;

    public static void SetEnabled(bool enabled)
    {
        Controller.SetEnabled(enabled);
    }

    static AutostartController CreateController()
    {
        if (OperatingSystem.IsWindows())
        {
            return new AutostartController(new WindowsAutostartBackend(
                new RegistryStartupValueStore(),
                () => Environment.ProcessPath));
        }

        return new AutostartController(new XdgAutostartBackend());
    }
}

sealed class AutostartController
{
    readonly IAutostartBackend _backend;

    public AutostartController(IAutostartBackend backend)
    {
        _backend = backend;
    }

    public bool IsEnabled => _backend.IsEnabled;

    public void SetEnabled(bool enabled)
    {
        _backend.SetEnabled(enabled);
    }
}

interface IAutostartBackend
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

sealed class XdgAutostartBackend : IAutostartBackend
{
    static string AutostartDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "autostart");

    static string EntryPath => Path.Combine(AutostartDirectory, "megaschoen-sessions.desktop");

    public bool IsEnabled => File.Exists(EntryPath);

    public void SetEnabled(bool enabled)
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

    static string LauncherPath()
    {
        var launcher = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "bin", "megaschoen-sessions");
        return File.Exists(launcher) ? launcher : Environment.ProcessPath ?? launcher;
    }
}
