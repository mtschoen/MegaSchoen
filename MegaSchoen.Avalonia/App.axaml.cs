using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace MegaSchoen.Avalonia;

public class App : Application
{
    static WindowsPlatformIntegration? _windowsIntegration;
    MainWindow? _mainWindow;

    internal ShutdownCoordinator Shutdown { get; } = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        DisplayManager.Core.DiagnosticLog.Sink = Claude.Core.Logger.Log;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Tray-resident monitor: closing the window never terminates the
            // app; only the tray's Exit calls Shutdown(). Explicit mode also
            // lets --hidden (the autostart entry) run with no window shown.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.ShutdownRequested += (_, _) => Shutdown.RequestShutdown();

            _mainWindow = new MainWindow();
            _ = StartWindowsIntegrationAsync();
            var startHidden = desktop.Args?.Contains("--hidden", StringComparer.OrdinalIgnoreCase) == true;
            if (!startHidden)
            {
                _mainWindow.Show();
            }
            SetUpTrayIcon(desktop);
            desktop.Exit += (_, _) => DisposeWindowsIntegration();
        }

        base.OnFrameworkInitializationCompleted();
    }

    async Task StartWindowsIntegrationAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _windowsIntegration = new WindowsPlatformIntegration(ShowMainWindow);
            await _windowsIntegration.StartAsync();
        }
        catch (Exception exception)
        {
            Claude.Core.Logger.Log($"Windows platform integration failed to start: {exception}");
        }
    }

    static void DisposeWindowsIntegration()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _windowsIntegration?.Dispose();
        _windowsIntegration = null;
    }

    internal static Task RefreshDisplayHotkeysAsync()
    {
        if (!OperatingSystem.IsWindows() || _windowsIntegration is null)
        {
            return Task.CompletedTask;
        }

        return _windowsIntegration.StartAsync();
    }

    // Tray icon (KDE StatusNotifier on Linux): the dashboard is a background
    // monitor like the Windows MAUI app, so the window hides to the tray on
    // close and only the tray's Exit really quits.
    void SetUpTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var showItem = new NativeMenuItem("Show sessions");
        showItem.Click += (_, _) => ShowMainWindow();

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            Shutdown.RequestShutdown();
            desktop.Shutdown();
        };

        var trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://MegaSchoen.Avalonia/Assets/appicon.png"))),
            ToolTipText = "MegaSchoen - Claude Sessions",
            Menu = new NativeMenu { Items = { showItem, exitItem } }
        };
        trayIcon.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, [trayIcon]);
    }

    void ShowMainWindow()
    {
        if (_mainWindow is not { } window) return;
        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();
    }
}
