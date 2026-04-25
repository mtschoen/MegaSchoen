using ClaudeCycler.Core;

namespace ClaudeHookBridge.Commands;

public static class CheckCommand
{
    public static int Run()
    {
        var bridgeExePath = Environment.ProcessPath ?? "";
        var installer = new SettingsJsonInstaller();
        var status = installer.GetStatus(bridgeExePath);

        Console.WriteLine($"Bridge exe:     {bridgeExePath}");
        Console.WriteLine($"Settings file:  {Paths.ClaudeSettingsFile}");
        Console.WriteLine();
        Print("Notification",     status.Notification,     status.NotificationPath);
        Print("UserPromptSubmit", status.UserPromptSubmit, status.UserPromptSubmitPath);
        Print("Stop",             status.Stop,             status.StopPath);
        Print("PostToolUse",      status.PostToolUse,      status.PostToolUsePath);
        Print("SessionEnd",       status.SessionEnd,       status.SessionEndPath);

        var allInstalled =
            status.Notification == InstallState.InstalledHere &&
            status.UserPromptSubmit == InstallState.InstalledHere &&
            status.Stop == InstallState.InstalledHere &&
            status.PostToolUse == InstallState.InstalledHere &&
            status.SessionEnd == InstallState.InstalledHere;

        return allInstalled ? 0 : 2;
    }

    static void Print(string eventName, InstallState state, string? path)
    {
        var label = state switch
        {
            InstallState.InstalledHere => "INSTALLED (this binary)",
            InstallState.InstalledElsewhere => $"INSTALLED AT DIFFERENT PATH: {path}",
            _ => "NOT INSTALLED"
        };
        Console.WriteLine($"  {eventName,-18} {label}");
    }
}
