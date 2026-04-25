using System.Runtime.InteropServices;
using DisplayManager.Core.Models;
using static MegaSchoen.Platforms.Windows.Services.Win32Interop;

namespace MegaSchoen.Platforms.Windows.Services;

/// <summary>
/// Manages the system tray icon with context menu for profile selection.
/// </summary>
sealed class TrayIconService : IDisposable
{
    const int TrayIconId = 1;
    const int MenuIdOpen = 1000;
    const int MenuIdExit = 1001;
    const int MenuIdInstallClaudeHooks = 1002;
    const int MenuIdCycleClaude = 1003;
    const int MenuIdClearNeedyClaude = 1004;
    const int MenuIdProfileBase = 2000;

    readonly MessageWindow _messageWindow;
    NOTIFYICONDATAW _iconData;
    IntPtr _hIcon;
    bool _iconCreated;
    bool _disposed;

    List<SavedDisplayProfile> _profiles = [];

    /// <summary>
    /// Fired when a profile is selected from the context menu. Parameter is the profile ID.
    /// </summary>
    public event EventHandler<Guid>? ProfileSelected;

    /// <summary>
    /// Fired when "Open MegaSchoen" is selected from the context menu.
    /// </summary>
    public event EventHandler? ShowRequested;

    /// <summary>
    /// Fired when "Exit" is selected from the context menu.
    /// </summary>
    public event EventHandler? ExitRequested;

    public event EventHandler? InstallClaudeHooksRequested;

    public event EventHandler? CycleClaudeRequested;

    public event EventHandler? ClearNeedyClaudeRequested;

    public TrayIconService(MessageWindow messageWindow)
    {
        _messageWindow = messageWindow;
        _messageWindow.TrayIconLeftClicked += OnTrayIconLeftClicked;
        _messageWindow.TrayIconRightClicked += OnTrayIconRightClicked;
    }

    /// <summary>
    /// Initializes the tray icon with the given profiles.
    /// </summary>
    public void Initialize(List<SavedDisplayProfile> profiles)
    {
        _profiles = profiles;
        CreateTrayIcon();
    }

    /// <summary>
    /// Updates the context menu with new profiles.
    /// </summary>
    public void UpdateProfiles(List<SavedDisplayProfile> profiles)
    {
        _profiles = profiles;
    }

    /// <summary>
    /// Shows a balloon notification.
    /// </summary>
    public void ShowNotification(string title, string message, NotificationIcon icon = NotificationIcon.Info)
    {
        if (!_iconCreated)
        {
            return;
        }

        _iconData.uFlags = NIF_INFO;
        _iconData.szInfoTitle = title.Length > 63 ? title[..63] : title;
        _iconData.szInfo = message.Length > 255 ? message[..255] : message;
        _iconData.dwInfoFlags = icon switch
        {
            NotificationIcon.Warning => NIIF_WARNING,
            NotificationIcon.Error => NIIF_ERROR,
            _ => NIIF_INFO
        };

        Shell_NotifyIcon(NIM_MODIFY, ref _iconData);

        // Reset flags
        _iconData.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    }

    void CreateTrayIcon()
    {
        if (_iconCreated)
        {
            return;
        }

        _hIcon = LoadApplicationIcon();

        _iconData = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _messageWindow.Handle,
            uID = TrayIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "MegaSchoen Display Manager"
        };

        _iconCreated = Shell_NotifyIcon(NIM_ADD, ref _iconData);
    }

    IntPtr LoadApplicationIcon()
    {
        // Preferred: load the MAUI-generated appicon.ico that sits next to the exe.
        // Unpackaged MAUI builds don't embed the icon as a Win32 resource in the exe,
        // so ExtractIconEx against Environment.ProcessPath returns nothing — we'd
        // end up with the shell32 monitor fallback. Loading the .ico file directly
        // lets Windows pick the right size from its multi-resolution frames.
        var icoPath = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
        if (File.Exists(icoPath))
        {
            var hIcon = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            if (hIcon != IntPtr.Zero)
            {
                return hIcon;
            }
        }

        // Fallback: extract from the exe (works if a future packaged build embeds the icon).
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            var largeIcons = new IntPtr[1];
            var smallIcons = new IntPtr[1];
            var count = ExtractIconEx(exePath, 0, largeIcons, smallIcons, 1);
            if (count > 0 && smallIcons[0] != IntPtr.Zero)
            {
                return smallIcons[0];
            }
            if (count > 0 && largeIcons[0] != IntPtr.Zero)
            {
                return largeIcons[0];
            }
        }

        // Last resort: shell32.dll default icon
        var shell32Icons = new IntPtr[1];
        ExtractIconEx("shell32.dll", 15, shell32Icons, null!, 1); // Monitor icon
        return shell32Icons[0];
    }

    void OnTrayIconLeftClicked(object? sender, EventArgs e)
    {
        ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    void OnTrayIconRightClicked(object? sender, POINT point)
    {
        ShowContextMenu(point);
    }

    void ShowContextMenu(POINT point)
    {
        var hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            uint position = 0;

            // Add profiles with hotkey hints
            for (var i = 0; i < _profiles.Count; i++)
            {
                var profile = _profiles[i];
                var text = profile.Name;

                // Add hotkey hint if defined
                if (profile.Hotkey?.Enabled == true)
                {
                    var hotkeyText = FormatHotkey(profile.Hotkey);
                    text = $"{profile.Name}\t{hotkeyText}";
                }

                InsertMenu(hMenu, position++, MF_STRING, (nuint)(MenuIdProfileBase + i), text);
            }

            if (_profiles.Count > 0)
            {
                InsertMenu(hMenu, position++, MF_SEPARATOR, 0, null);
            }

            InsertMenu(hMenu, position++, MF_STRING, MenuIdOpen, "Open MegaSchoen");
            InsertMenu(hMenu, position++, MF_STRING, MenuIdCycleClaude, "Cycle Claude Now");
            InsertMenu(hMenu, position++, MF_STRING, MenuIdInstallClaudeHooks, "Install Claude Hooks");
            InsertMenu(hMenu, position++, MF_STRING, MenuIdClearNeedyClaude, "Clear Needy Sessions");
            InsertMenu(hMenu, position++, MF_SEPARATOR, 0, null);
            InsertMenu(hMenu, position, MF_STRING, MenuIdExit, "Exit");

            // Required for TrackPopupMenu to work correctly
            SetForegroundWindow(_messageWindow.Handle);

            var cmd = TrackPopupMenu(
                hMenu,
                TPM_LEFTALIGN | TPM_RETURNCMD | TPM_NONOTIFY,
                point.x,
                point.y,
                0,
                _messageWindow.Handle,
                IntPtr.Zero);

            // Post a null message to force menu to close properly
            PostMessage(_messageWindow.Handle, 0, IntPtr.Zero, IntPtr.Zero);

            HandleMenuCommand(cmd);
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    void HandleMenuCommand(int cmd)
    {
        if (cmd == 0)
        {
            return;
        }

        if (cmd == MenuIdOpen)
        {
            ShowRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (cmd == MenuIdExit)
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (cmd == MenuIdCycleClaude)
        {
            CycleClaudeRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (cmd == MenuIdInstallClaudeHooks)
        {
            InstallClaudeHooksRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (cmd == MenuIdClearNeedyClaude)
        {
            ClearNeedyClaudeRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (cmd >= MenuIdProfileBase)
        {
            var index = cmd - MenuIdProfileBase;
            if (index >= 0 && index < _profiles.Count)
            {
                ProfileSelected?.Invoke(this, _profiles[index].Id);
            }
        }
    }

    static string FormatHotkey(HotkeyDefinition hotkey)
    {
        var parts = new List<string>();
        foreach (var mod in hotkey.Modifiers)
        {
            parts.Add(mod switch
            {
                "Control" => "Ctrl",
                _ => mod
            });
        }
        parts.Add(hotkey.Key);
        return string.Join("+", parts);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_iconCreated)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _iconData);
            _iconCreated = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }

        _messageWindow.TrayIconLeftClicked -= OnTrayIconLeftClicked;
        _messageWindow.TrayIconRightClicked -= OnTrayIconRightClicked;
    }
}

public enum NotificationIcon
{
    Info,
    Warning,
    Error
}
