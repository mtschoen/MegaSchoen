using DisplayManager.Core.Services;
using MegaSchoen.Platforms.Windows.Services;
using Microsoft.UI.Xaml;

namespace MegaSchoen.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
    SingleInstanceService? _singleInstance;

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        // Check for single instance before initializing
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquire())
        {
            // Another instance is already running - it will be signaled to show
            Environment.Exit(0);
            return;
        }

        this.InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        // Initialize tray icon and hotkeys after services are ready
        InitializeWindowsServices();
    }

    void InitializeWindowsServices()
    {
        var services = MauiWinUIApplication.Current.Services;

        var messageWindow = services.GetRequiredService<MessageWindow>();
        var tray = services.GetRequiredService<TrayIconService>();
        var hotkeys = services.GetRequiredService<GlobalHotkeyService>();
        var claudeWindowService = services.GetRequiredService<ClaudeWindowService>();
        var profileService = services.GetRequiredService<DisplayProfileService>();

        // Load profiles synchronously (Task.Run avoids UI thread deadlock)
        var profiles = Task.Run(() => profileService.GetAllProfilesAsync()).Result;
        tray.Initialize(profiles);
        hotkeys.RefreshFromProfiles(profiles);

        // Wire up tray icon events
        tray.ProfileSelected += (s, profileId) =>
        {
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile != null)
            {
                var result = profileService.ApplyProfile(profile);
                if (result.Success)
                {
                    tray.ShowNotification("Profile Applied", $"'{profile.Name}' applied successfully.");
                }
                else
                {
                    tray.ShowNotification("Profile Failed", $"Failed to apply '{profile.Name}'.", NotificationIcon.Error);
                }
            }
        };

        tray.ShowRequested += (s, e) =>
        {
            ShowMainWindow();
        };

        tray.ExitRequested += (s, e) =>
        {
            // Dispose services and exit
            tray.Dispose();
            hotkeys.Dispose();
            messageWindow.Dispose();
            _singleInstance?.Dispose();
            Environment.Exit(0);
        };

        tray.InstallClaudeHooksRequested += (s, e) =>
        {
            try
            {
                var bridgePath = Path.Combine(AppContext.BaseDirectory, "ClaudeHookBridge.exe");
                var installer = new ClaudeCycler.Core.SettingsJsonInstaller();
                installer.Install(bridgePath);
                tray.ShowNotification("MegaSchoen", "Claude hooks installed");
            }
            catch (Exception exception)
            {
                tray.ShowNotification("MegaSchoen", $"Install failed: {exception.Message}", NotificationIcon.Error);
            }
        };

        tray.CycleClaudeRequested += (s, e) =>
        {
            try
            {
                claudeWindowService.CycleToNext();
            }
            catch (Exception exception)
            {
                ClaudeCycler.Core.Logger.Log($"CycleClaudeRequested threw: {exception}");
                tray.ShowNotification("MegaSchoen", $"Cycle failed: {exception.Message}", NotificationIcon.Error);
            }
        };

        tray.ClearNeedyClaudeRequested += (s, e) =>
        {
            try
            {
                var store = new ClaudeCycler.Core.StateStore();
                store.Write(new ClaudeCycler.Core.Models.NeedySessionsFile());
                tray.ShowNotification("MegaSchoen", "Needy sessions cleared");
            }
            catch (Exception exception)
            {
                ClaudeCycler.Core.Logger.Log($"ClearNeedyClaude threw: {exception}");
                tray.ShowNotification("MegaSchoen", $"Clear failed: {exception.Message}", NotificationIcon.Error);
            }
        };

        // Wire up hotkey events
        hotkeys.HotkeyTriggered += (s, profileId) =>
        {
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile != null)
            {
                var result = profileService.ApplyProfile(profile);
                if (result.Success)
                {
                    tray.ShowNotification("Profile Applied", $"'{profile.Name}' applied via hotkey.");
                }
                else
                {
                    tray.ShowNotification("Profile Failed", $"Failed to apply '{profile.Name}'.", NotificationIcon.Error);
                }
            }
        };

        hotkeys.RegisterNamedHotkey("claude-cycle", "0", new[] { "Control", "Alt" });
        hotkeys.NamedHotkeyTriggered += (s, name) =>
        {
            if (name == "claude-cycle")
            {
                try
                {
                    claudeWindowService.CycleToNext();
                }
                catch (Exception exception)
                {
                    ClaudeCycler.Core.Logger.Log($"CycleToNext threw: {exception}");
                    tray.ShowNotification("MegaSchoen", $"Cycle failed: {exception.Message}", NotificationIcon.Error);
                }
            }
        };

        // Wire up activation message for single instance
        messageWindow.CustomMessageReceived += (s, msg) =>
        {
            ShowMainWindow();
        };

        // Check for --minimized argument
        var cmdArgs = Environment.GetCommandLineArgs();
        var startMinimized = cmdArgs.Contains("--minimized");

        // Set up window close interception after window is ready
        Task.Run(async () =>
        {
            await Task.Delay(100);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
                if (window != null)
                {
                    var mauiWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                    if (mauiWindow != null)
                    {
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mauiWindow);
                        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                        // Intercept close to minimize to tray instead
                        appWindow.Closing += (s, e) =>
                        {
                            e.Cancel = true;
                            HideWindow(hwnd);
                            tray.ShowNotification("MegaSchoen", "Application minimized to tray. Right-click the tray icon for options.");
                        };

                        // Start minimized if requested
                        if (startMinimized)
                        {
                            HideWindow(hwnd);
                        }
                    }
                }
            });
        });
    }

    void ShowMainWindow()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            if (window != null)
            {
                var mauiWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                if (mauiWindow != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mauiWindow);
                    ShowWindow(hwnd);
                    SetForegroundWindow(hwnd);
                }
            }
        });
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool ShowWindowNative(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    static void ShowWindow(IntPtr hwnd) => ShowWindowNative(hwnd, 9); // SW_RESTORE = 9
    static void HideWindow(IntPtr hwnd) => ShowWindowNative(hwnd, 0); // SW_HIDE = 0
}
