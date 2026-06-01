using Claude.Core.Models;
using DisplayManager.Core.Models;
using DisplayManager.Core.Services;
using MegaSchoen.Platforms.Windows.Services;
using Microsoft.UI.Xaml;

namespace MegaSchoen.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
    // Process-global single-instance guard: the mutex must live for the whole process,
    // so it is held in a static field (not an instance field that would make App own a
    // disposable and require IDisposable on a WinUI Application).
    static SingleInstanceService? _singleInstance;
    static readonly string[] CtrlAlt = ["Control", "Alt"];

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
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

        InitializeWindowsServices();
    }

    static void InitializeWindowsServices()
    {
        var services = MauiWinUIApplication.Current.Services;

        MigrateAndSweepSessionState(services);
        CheckBridgeFreshness();

        var messageWindow = services.GetRequiredService<MessageWindow>();
        var tray = services.GetRequiredService<TrayIconService>();
        var hotkeys = services.GetRequiredService<GlobalHotkeyService>();
        var claudeWindowService = services.GetRequiredService<ClaudeWindowService>();
        var profileService = services.GetRequiredService<DisplayProfileService>();

        // Load profiles synchronously (Task.Run avoids UI thread deadlock)
        var profiles = Task.Run(() => profileService.GetAllProfilesAsync()).Result;
        tray.Initialize(profiles);
        hotkeys.RefreshFromProfiles(profiles);

        WireTrayEvents(tray, hotkeys, messageWindow, claudeWindowService, profileService, profiles);
        WireHotkeyEvents(hotkeys, tray, claudeWindowService, profileService, profiles);
        WireWindowLifecycle(messageWindow, tray);
    }

    static void WireTrayEvents(
        TrayIconService tray, GlobalHotkeyService hotkeys, MessageWindow messageWindow,
        ClaudeWindowService claudeWindowService, DisplayProfileService profileService,
        List<SavedDisplayProfile> profiles)
    {
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
                var installer = new Claude.Core.SettingsJsonInstaller();
                installer.Install(bridgePath);
                tray.ShowNotification("MegaSchoen", "Claude hooks installed");
            }
            catch (Exception exception)
            {
                tray.ShowNotification("MegaSchoen", $"Install failed: {exception.Message}", NotificationIcon.Error);
            }
        };

        tray.CyclePermissionsRequested += (s, e) =>
        {
            try
            {
                claudeWindowService.CycleToNext(WaitingReason.Permission);
            }
            catch (Exception exception)
            {
                Claude.Core.Logger.Log($"CyclePermissionsRequested threw: {exception}");
                tray.ShowNotification("MegaSchoen", $"Cycle failed: {exception.Message}", NotificationIcon.Error);
            }
        };

        tray.CycleAnyWaitingRequested += (s, e) =>
        {
            try
            {
                claudeWindowService.CycleToNext(filter: null);
            }
            catch (Exception exception)
            {
                Claude.Core.Logger.Log($"CycleAnyWaitingRequested threw: {exception}");
                tray.ShowNotification("MegaSchoen", $"Cycle failed: {exception.Message}", NotificationIcon.Error);
            }
        };

        tray.ClearNeedyClaudeRequested += (s, e) =>
        {
            try
            {
                var store = new Claude.Core.StateStore();
                store.DeleteAll();
                tray.ShowNotification("MegaSchoen", "Needy sessions cleared");
            }
            catch (Exception exception)
            {
                Claude.Core.Logger.Log($"ClearNeedyClaude threw: {exception}");
                tray.ShowNotification("MegaSchoen", $"Clear failed: {exception.Message}", NotificationIcon.Error);
            }
        };
    }

    static void WireHotkeyEvents(
        GlobalHotkeyService hotkeys, TrayIconService tray,
        ClaudeWindowService claudeWindowService, DisplayProfileService profileService,
        List<SavedDisplayProfile> profiles)
    {
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

        if (!hotkeys.RegisterNamedHotkey("claude-cycle-perms", "9", CtrlAlt))
        {
            Claude.Core.Logger.Log("Failed to register Ctrl+Alt+9 (claude-cycle-perms) — likely already bound system-wide");
        }
        if (!hotkeys.RegisterNamedHotkey("claude-cycle-any", "0", CtrlAlt))
        {
            Claude.Core.Logger.Log("Failed to register Ctrl+Alt+0 (claude-cycle-any) — likely already bound system-wide");
        }

        hotkeys.NamedHotkeyTriggered += (s, name) =>
        {
            WaitingReason? filter = name switch
            {
                "claude-cycle-perms" => WaitingReason.Permission,
                "claude-cycle-any" => null,
                _ => null
            };

            if (name is not ("claude-cycle-perms" or "claude-cycle-any"))
            {
                return;
            }

            try
            {
                claudeWindowService.CycleToNext(filter);
            }
            catch (Exception exception)
            {
                Claude.Core.Logger.Log($"CycleToNext threw: {exception}");
                tray.ShowNotification("MegaSchoen", $"Cycle failed: {exception.Message}", NotificationIcon.Error);
            }
        };
    }

    static void WireWindowLifecycle(MessageWindow messageWindow, TrayIconService tray)
    {
        // Wire up activation message for single instance
        messageWindow.CustomMessageReceived += (s, msg) =>
        {
            ShowMainWindow();
        };

        var cmdArgs = Environment.GetCommandLineArgs();
        var startMinimized = cmdArgs.Contains("--minimized");

        Task.Run(async () =>
        {
            await Task.Delay(100);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var window = Microsoft.Maui.Controls.Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
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

    static void MigrateAndSweepSessionState(IServiceProvider services)
    {
        try
        {
            if (File.Exists(Claude.Core.Paths.LegacyNeedySessionsFile))
            {
                File.Delete(Claude.Core.Paths.LegacyNeedySessionsFile);
                Claude.Core.Logger.Log("Deleted legacy needy-sessions.json (one-time migration)");
            }
        }
        catch (Exception exception)
        {
            Claude.Core.Logger.Log($"Legacy state-file migration failed: {exception.Message}");
        }

        try
        {
            Claude.Core.Paths.EnsureNeedySessionsDirectoryExists();
            var enumerator = services.GetRequiredService<Claude.Core.ActiveSessionEnumerator>();
            var store = services.GetRequiredService<Claude.Core.StateStore>();
            // Liveness is now centralized in ActiveSessionEnumerator (a session
            // is live iff a claude process runs in its cwd). The sweep simply
            // deletes StateStore entries the enumerator no longer considers live.
            var liveIds = new HashSet<string>(
                enumerator.Enumerate().Select(s => s.SessionId),
                StringComparer.OrdinalIgnoreCase);
            var swept = 0;
            foreach (var existingId in store.EnumerateSessionIds())
            {
                if (!liveIds.Contains(existingId))
                {
                    store.Delete(existingId);
                    swept++;
                }
            }
            if (swept > 0)
            {
                Claude.Core.Logger.Log($"Startup zombie sweep removed {swept} stale session entries");
            }
        }
        catch (Exception exception)
        {
            Claude.Core.Logger.Log($"Startup zombie sweep failed: {exception.Message}");
        }
    }

    // Stale-binary guardrail: settings.json runs the MAUI-embedded ClaudeHookBridge
    // copy. If its version diverges from the app's, the embedded copy was not
    // rebuilt and status detection will be wrong (the 35k stale-event incident).
    // App and bridge are built from the same tree, so their version stamps match
    // unless the CopyClaudeHookBridge target failed to refresh the embedded copy.
    static void CheckBridgeFreshness()
    {
        var appVersion = Claude.Core.BuildInfo.VersionFor(typeof(App).Assembly);
        // The CopyClaudeHookBridge target drops ClaudeHookBridge.dll next to the app.
        var bridgeDll = Path.Combine(AppContext.BaseDirectory, "ClaudeHookBridge.dll");
        var bridgeVersion = Claude.Core.BuildInfo.VersionOfFile(bridgeDll);

        // Compare the git stamp, not the full version: the MAUI app's SemVer
        // prefix comes from ApplicationDisplayVersion (1.0) while the bridge's
        // comes from Directory.Build.props (0.1.0), so they never match as whole
        // strings. The "<hash>[-dirty]" suffix is the real same-commit signal.
        if (Claude.Core.BuildInfo.BuildStamp(bridgeVersion) == Claude.Core.BuildInfo.BuildStamp(appVersion))
        {
            return;
        }

        Claude.Core.Logger.Log($"STALE BRIDGE: app={appVersion} bridge={bridgeVersion}");
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var page = Microsoft.Maui.Controls.Application.Current?.Windows is { Count: > 0 } windows ? windows[0].Page : null;
            if (page is not null)
            {
                await page.DisplayAlertAsync(
                    "Hook bridge is stale",
                    $"App is {appVersion} but the embedded ClaudeHookBridge is {bridgeVersion}. " +
                    "Rebuild the solution (VS18 MSBuild) so session status detection is correct.",
                    "OK");
            }
        });
    }

    static void ShowMainWindow()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var window = Microsoft.Maui.Controls.Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
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
