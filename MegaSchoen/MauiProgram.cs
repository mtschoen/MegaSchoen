using DisplayManager.Core.Services;
#if DEBUG
using Microsoft.Extensions.Logging;
#endif
#if WINDOWS
using Claude.Core;
using Claude.Core.Windows;
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
        builder.Services.AddSingleton<IClaudeWindowFocuser, WindowsClaudeWindowFocuser>();
        builder.Services.AddSingleton<ISshSessionWindowResolver, WindowsSshSessionWindowResolver>();
        builder.Services.AddSingleton<ClaudeWindowService>();
        builder.Services.AddSingleton<IClaudeProcessLocator, WindowsClaudeProcessLocator>();
        builder.Services.AddSingleton<StateStore>();
        builder.Services.AddSingleton<ISessionSource, ClaudeSessionSource>();
        builder.Services.AddSingleton<ActiveSessionEnumerator>(services =>
            new ActiveSessionEnumerator(services.GetServices<ISessionSource>()));
        builder.Services.AddTransient<ViewModels.SessionsPageViewModel>();
        builder.Services.AddTransient<SessionsPage>();
        builder.Services.AddTransient<DisplayManagerPage>();
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
