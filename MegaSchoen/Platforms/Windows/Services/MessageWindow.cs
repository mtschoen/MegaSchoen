using System.Runtime.InteropServices;
using static MegaSchoen.Platforms.Windows.Services.Win32Interop;

namespace MegaSchoen.Platforms.Windows.Services;

/// <summary>
/// Hidden message-only window for receiving Win32 messages (hotkeys, tray icon clicks).
/// </summary>
sealed class MessageWindow : IDisposable
{
    const string WindowClassName = "MegaSchoen_MessageWindow";

    readonly WndProc _wndProcDelegate;
    IntPtr _hWnd;
    bool _disposed;

    /// <summary>
    /// Fired when a registered hotkey is pressed. Parameter is the hotkey ID.
    /// </summary>
    public event EventHandler<int>? HotkeyPressed;

    /// <summary>
    /// Fired when the tray icon is left-clicked.
    /// </summary>
    public event EventHandler? TrayIconLeftClicked;

    /// <summary>
    /// Fired when the tray icon is right-clicked. Point is the cursor position.
    /// </summary>
    public event EventHandler<POINT>? TrayIconRightClicked;

    /// <summary>
    /// Fired when a custom message is received (for single-instance activation).
    /// </summary>
    public event EventHandler<uint>? CustomMessageReceived;

    /// <summary>
    /// Handle to the message window.
    /// </summary>
    public IntPtr Handle => _hWnd;

    /// <summary>
    /// Custom message ID for single-instance activation.
    /// </summary>
    public uint ActivateMessage { get; }

    public MessageWindow()
    {
        _wndProcDelegate = WndProcHandler;
        ActivateMessage = RegisterWindowMessage("MegaSchoen_Activate");

        CreateMessageWindow();
    }

    void CreateMessageWindow()
    {
        var hInstance = GetModuleHandle(null);

        var wndClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = WindowClassName
        };

        var classAtom = RegisterClassEx(ref wndClass);
        if (classAtom == 0)
        {
            var error = GetLastError();
            if (error != 1410) // ERROR_CLASS_ALREADY_EXISTS
            {
                throw new InvalidOperationException($"Failed to register window class: {error}");
            }
        }

        _hWnd = CreateWindowEx(
            0,
            WindowClassName,
            "MegaSchoen Message Window",
            0,
            0, 0, 0, 0,
            HWND_MESSAGE,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_hWnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to create message window: {GetLastError()}");
        }
    }

    IntPtr WndProcHandler(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_HOTKEY:
                HotkeyPressed?.Invoke(this, (int)wParam);
                return IntPtr.Zero;

            case WM_TRAYICON:
                var trayMsg = (int)lParam;
                if (trayMsg == WM_LBUTTONUP)
                {
                    TrayIconLeftClicked?.Invoke(this, EventArgs.Empty);
                }
                else if (trayMsg == WM_RBUTTONUP)
                {
                    GetCursorPos(out var point);
                    TrayIconRightClicked?.Invoke(this, point);
                }
                return IntPtr.Zero;

            case WM_COMMAND:
                // Menu command - handled by TrayIconService
                return IntPtr.Zero;

            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;

            default:
                // Check for custom activation message
                if (msg == ActivateMessage)
                {
                    CustomMessageReceived?.Invoke(this, msg);
                    return IntPtr.Zero;
                }
                break;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Posts the activation message to this window (for single-instance communication).
    /// </summary>
    public void PostActivateMessage()
    {
        PostMessage(_hWnd, ActivateMessage, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Finds the existing message window by class name and posts activation message.
    /// </summary>
    public static void SignalExistingInstance()
    {
        var existingHwnd = FindWindow(WindowClassName, null);
        if (existingHwnd != IntPtr.Zero)
        {
            var activateMsg = RegisterWindowMessage("MegaSchoen_Activate");
            PostMessage(existingHwnd, activateMsg, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hWnd != IntPtr.Zero)
        {
            DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }
    }
}
