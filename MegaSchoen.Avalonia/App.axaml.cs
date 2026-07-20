using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace MegaSchoen.Avalonia;

public partial class App : Application
{
    // Set by the tray Exit item so MainWindow's hide-on-close intercept lets
    // the real shutdown through.
    internal bool ExitRequested;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            SetUpTrayIcon(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Tray icon (KDE StatusNotifier on Linux): the dashboard is a background
    // monitor like the Windows MAUI app, so the window hides to the tray on
    // close and only the tray's Exit really quits.
    void SetUpTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var showItem = new NativeMenuItem("Show sessions");
        showItem.Click += (_, _) => ShowMainWindow(desktop);

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            ExitRequested = true;
            desktop.Shutdown();
        };

        var trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://MegaSchoen.Avalonia/Assets/appicon.png"))),
            ToolTipText = "MegaSchoen - Claude Sessions",
            Menu = new NativeMenu { Items = { showItem, exitItem } }
        };
        trayIcon.Clicked += (_, _) => ShowMainWindow(desktop);

        TrayIcon.SetIcons(this, [trayIcon]);
    }

    static void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow is not { } window) return;
        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();
    }
}
