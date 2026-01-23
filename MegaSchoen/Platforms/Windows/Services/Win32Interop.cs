using System.Runtime.InteropServices;

namespace MegaSchoen.Platforms.Windows.Services;

/// <summary>
/// P/Invoke declarations for Win32 APIs used by tray icon, hotkeys, and message window.
/// </summary>
static partial class Win32Interop
{
    // Window messages
    public const int WM_DESTROY = 0x0002;
    public const int WM_CLOSE = 0x0010;
    public const int WM_COMMAND = 0x0111;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_USER = 0x0400;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_CONTEXTMENU = 0x007B;

    // Tray icon message (WM_USER + 1)
    public const int WM_TRAYICON = WM_USER + 1;

    // Hotkey modifiers
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // Virtual key codes
    public const int VK_ESCAPE = 0x1B;

    // Shell_NotifyIcon messages
    public const int NIM_ADD = 0x00;
    public const int NIM_MODIFY = 0x01;
    public const int NIM_DELETE = 0x02;

    // NOTIFYICONDATA flags
    public const int NIF_MESSAGE = 0x01;
    public const int NIF_ICON = 0x02;
    public const int NIF_TIP = 0x04;
    public const int NIF_INFO = 0x10;

    // NOTIFYICONDATA info flags (balloon icons)
    public const int NIIF_NONE = 0x00;
    public const int NIIF_INFO = 0x01;
    public const int NIIF_WARNING = 0x02;
    public const int NIIF_ERROR = 0x03;

    // Menu flags
    public const int MF_STRING = 0x0000;
    public const int MF_SEPARATOR = 0x0800;
    public const int MF_POPUP = 0x0010;

    // TrackPopupMenu flags
    public const uint TPM_LEFTALIGN = 0x0000;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_NONOTIFY = 0x0080;

    // Window styles
    public const int WS_OVERLAPPED = 0x00000000;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_CHILD = 0x40000000;

    // Extended window styles
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    // Special window handle for message-only windows
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    // SetWindowLongPtr indices
    public const int GWLP_WNDPROC = -4;

    // Keyboard hook
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    // NOTIFYICONDATAW structure (Windows Vista+)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Shell32
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATAW lpData);

    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, int nIcons);

    // User32 - Window management
    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEXW lpwcx);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial IntPtr DispatchMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    public static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    // User32 - Hotkeys
    [LibraryImport("user32.dll", EntryPoint = "RegisterHotKey")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterHotKey")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    // User32 - Menu
    [LibraryImport("user32.dll", EntryPoint = "CreatePopupMenu")]
    public static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "DestroyMenu")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(IntPtr hMenu);

    [LibraryImport("user32.dll", EntryPoint = "InsertMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [LibraryImport("user32.dll", EntryPoint = "TrackPopupMenu")]
    public static partial int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    // User32 - Keyboard hook
    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    public static partial IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [LibraryImport("user32.dll", EntryPoint = "UnhookWindowsHookEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll", EntryPoint = "CallNextHookEx")]
    public static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetKeyState")]
    public static partial short GetKeyState(int nVirtKey);

    // User32 - Cross-process messaging
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string lpString);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    // User32 - Icons
    [LibraryImport("user32.dll", EntryPoint = "DestroyIcon")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr hIcon);

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    // LoadImage constants
    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x0010;
    public const uint LR_DEFAULTSIZE = 0x0040;

    // Kernel32
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandle(string? lpModuleName);

    [LibraryImport("kernel32.dll", EntryPoint = "GetLastError")]
    public static partial int GetLastError();

    /// <summary>
    /// Converts a modifier string to Win32 modifier flags.
    /// </summary>
    public static uint ModifierToFlag(string modifier) => modifier switch
    {
        "Control" => MOD_CONTROL,
        "Alt" => MOD_ALT,
        "Shift" => MOD_SHIFT,
        "Win" => MOD_WIN,
        _ => 0
    };

    /// <summary>
    /// Converts a key name to Win32 virtual key code.
    /// </summary>
    public static uint KeyToVirtualKey(string key)
    {
        // Numbers
        if (key.Length == 1 && char.IsDigit(key[0]))
        {
            return (uint)('0' + (key[0] - '0'));
        }

        // Letters
        if (key.Length == 1 && char.IsLetter(key[0]))
        {
            return (uint)char.ToUpperInvariant(key[0]);
        }

        // Function keys
        if (key.StartsWith("F") && int.TryParse(key.AsSpan(1), out var fNum) && fNum is >= 1 and <= 24)
        {
            return (uint)(0x70 + fNum - 1); // VK_F1 = 0x70
        }

        // Special keys
        return key switch
        {
            "Escape" => 0x1B,
            "Tab" => 0x09,
            "Space" => 0x20,
            "Enter" or "Return" => 0x0D,
            "Backspace" => 0x08,
            "Delete" => 0x2E,
            "Insert" => 0x2D,
            "Home" => 0x24,
            "End" => 0x23,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            "Left" => 0x25,
            "Up" => 0x26,
            "Right" => 0x27,
            "Down" => 0x28,
            "PrintScreen" => 0x2C,
            "Pause" => 0x13,
            "NumLock" => 0x90,
            "ScrollLock" => 0x91,
            "CapsLock" => 0x14,
            // Numpad
            "Numpad0" => 0x60,
            "Numpad1" => 0x61,
            "Numpad2" => 0x62,
            "Numpad3" => 0x63,
            "Numpad4" => 0x64,
            "Numpad5" => 0x65,
            "Numpad6" => 0x66,
            "Numpad7" => 0x67,
            "Numpad8" => 0x68,
            "Numpad9" => 0x69,
            "Multiply" => 0x6A,
            "Add" => 0x6B,
            "Subtract" => 0x6D,
            "Decimal" => 0x6E,
            "Divide" => 0x6F,
            // OEM keys
            "OemMinus" or "-" => 0xBD,
            "OemPlus" or "=" => 0xBB,
            "OemOpenBrackets" or "[" => 0xDB,
            "OemCloseBrackets" or "]" => 0xDD,
            "OemPipe" or "\\" => 0xDC,
            "OemSemicolon" or ";" => 0xBA,
            "OemQuotes" or "'" => 0xDE,
            "OemComma" or "," => 0xBC,
            "OemPeriod" or "." => 0xBE,
            "OemQuestion" or "/" => 0xBF,
            "OemTilde" or "`" => 0xC0,
            _ => 0
        };
    }

    /// <summary>
    /// Converts a virtual key code to a key name.
    /// </summary>
    public static string VirtualKeyToKeyName(int vkCode)
    {
        // Numbers
        if (vkCode is >= 0x30 and <= 0x39)
        {
            return ((char)vkCode).ToString();
        }

        // Letters
        if (vkCode is >= 0x41 and <= 0x5A)
        {
            return ((char)vkCode).ToString();
        }

        // Function keys
        if (vkCode is >= 0x70 and <= 0x87)
        {
            return $"F{vkCode - 0x70 + 1}";
        }

        // Special keys
        return vkCode switch
        {
            0x1B => "Escape",
            0x09 => "Tab",
            0x20 => "Space",
            0x0D => "Enter",
            0x08 => "Backspace",
            0x2E => "Delete",
            0x2D => "Insert",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x13 => "Pause",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            0x14 => "CapsLock",
            // Numpad
            >= 0x60 and <= 0x69 => $"Numpad{vkCode - 0x60}",
            0x6A => "Multiply",
            0x6B => "Add",
            0x6D => "Subtract",
            0x6E => "Decimal",
            0x6F => "Divide",
            // OEM keys
            0xBD => "-",
            0xBB => "=",
            0xDB => "[",
            0xDD => "]",
            0xDC => "\\",
            0xBA => ";",
            0xDE => "'",
            0xBC => ",",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            _ => $"0x{vkCode:X2}"
        };
    }

    /// <summary>
    /// Checks if a virtual key code is a modifier key.
    /// </summary>
    public static bool IsModifierKey(int vkCode) => vkCode is
        0xA0 or 0xA1 or // VK_LSHIFT, VK_RSHIFT
        0xA2 or 0xA3 or // VK_LCONTROL, VK_RCONTROL
        0xA4 or 0xA5 or // VK_LMENU (Alt), VK_RMENU
        0x5B or 0x5C or // VK_LWIN, VK_RWIN
        0x10 or 0x11 or 0x12; // VK_SHIFT, VK_CONTROL, VK_MENU (Alt)
}
