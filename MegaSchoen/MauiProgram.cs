using DisplayManager.Core.Services;
using Microsoft.Extensions.Logging;
#if WINDOWS
using MegaSchoen.Platforms.Windows.Services;
#endif

namespace MegaSchoen;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register core services
        builder.Services.AddSingleton<DisplayProfileService>();

#if WINDOWS
        // Register Windows-specific services
        builder.Services.AddSingleton<MessageWindow>();
        builder.Services.AddSingleton<TrayIconService>();
        builder.Services.AddSingleton<GlobalHotkeyService>();
        builder.Services.AddSingleton<KeyCaptureService>();
        builder.Services.AddSingleton<StartupService>();
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
