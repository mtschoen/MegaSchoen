using Avalonia;
using System;

namespace MegaSchoen.Avalonia;

static class Program
{
    [STAThread]
    public static void Main(string[] arguments) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(arguments);

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
