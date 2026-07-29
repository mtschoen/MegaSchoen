using Avalonia;
using System;
using System.Runtime.Versioning;
using Claude.Core;

namespace MegaSchoen.Avalonia;

static class Program
{
    [STAThread]
    public static int Main(string[] arguments)
    {
        if (StartupCommands.TryRun(
                arguments,
                Console.Out,
                Console.Error,
                () => BuildInfo.VersionFor(typeof(Program).Assembly),
                WindowsPackagingVerifier.VerifyCurrent,
                out var commandExitCode))
        {
            return commandExitCode;
        }

        if (OperatingSystem.IsWindows())
        {
            return RunWindows(arguments);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments);
        return 0;
    }

    [SupportedOSPlatform("windows")]
    static int RunWindows(string[] arguments)
    {
        using var singleInstance = new SingleInstanceGuard(
            new WindowsSingleInstanceLock(),
            WindowsMessageWindow.SignalExistingInstance);
        if (!singleInstance.TryAcquire())
        {
            return 0;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments);
        return 0;
    }

    // Also invoked by the Avalonia previewer/designer via reflection.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
