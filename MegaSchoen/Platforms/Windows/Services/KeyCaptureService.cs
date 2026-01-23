using System.Runtime.InteropServices;
using DisplayManager.Core.Models;
using static MegaSchoen.Platforms.Windows.Services.Win32Interop;

namespace MegaSchoen.Platforms.Windows.Services;

/// <summary>
/// Captures keyboard input for hotkey configuration using a low-level keyboard hook.
/// The hook is only active during capture mode.
/// </summary>
sealed class KeyCaptureService : IDisposable
{
    IntPtr _hookHandle;
    LowLevelKeyboardProc? _hookProc;
    bool _isCapturing;
    bool _disposed;

    readonly HashSet<string> _currentModifiers = [];

    /// <summary>
    /// Fired when a hotkey combination is captured (modifiers + key).
    /// </summary>
    public event EventHandler<HotkeyDefinition>? HotkeyCaptured;

    /// <summary>
    /// Fired when capture is cancelled (Escape pressed without modifiers).
    /// </summary>
    public event EventHandler? CaptureCancelled;

    /// <summary>
    /// Whether the service is currently capturing key input.
    /// </summary>
    public bool IsCapturing => _isCapturing;

    /// <summary>
    /// Starts capturing keyboard input.
    /// </summary>
    public void StartCapture()
    {
        if (_isCapturing)
        {
            return;
        }

        _currentModifiers.Clear();
        _hookProc = LowLevelKeyboardProcCallback;
        _hookHandle = SetWindowsHookEx(
            WH_KEYBOARD_LL,
            _hookProc,
            GetModuleHandle(null),
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to install keyboard hook: {GetLastError()}");
        }

        _isCapturing = true;
    }

    /// <summary>
    /// Stops capturing keyboard input.
    /// </summary>
    public void StopCapture()
    {
        if (!_isCapturing)
        {
            return;
        }

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _hookProc = null;
        _isCapturing = false;
        _currentModifiers.Clear();
    }

    IntPtr LowLevelKeyboardProcCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var vkCode = hookStruct.vkCode;
            var isKeyDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            var isKeyUp = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;

            if (IsModifierKey(vkCode))
            {
                var modifierName = GetModifierName(vkCode);
                if (isKeyDown)
                {
                    _currentModifiers.Add(modifierName);
                }
                else if (isKeyUp)
                {
                    _currentModifiers.Remove(modifierName);
                }
            }
            else if (isKeyDown)
            {
                // Non-modifier key pressed
                var keyName = VirtualKeyToKeyName(vkCode);

                // Check for Escape to cancel
                if (vkCode == VK_ESCAPE && _currentModifiers.Count == 0)
                {
                    StopCapture();
                    CaptureCancelled?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    // Capture the hotkey combination
                    var hotkey = new HotkeyDefinition
                    {
                        Modifiers = _currentModifiers.ToList(),
                        Key = keyName,
                        Enabled = true
                    };

                    StopCapture();
                    HotkeyCaptured?.Invoke(this, hotkey);
                }

                // Block this key from reaching other applications during capture
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    static string GetModifierName(int vkCode) => vkCode switch
    {
        0xA0 or 0xA1 or 0x10 => "Shift",   // VK_LSHIFT, VK_RSHIFT, VK_SHIFT
        0xA2 or 0xA3 or 0x11 => "Control", // VK_LCONTROL, VK_RCONTROL, VK_CONTROL
        0xA4 or 0xA5 or 0x12 => "Alt",     // VK_LMENU, VK_RMENU, VK_MENU
        0x5B or 0x5C => "Win",             // VK_LWIN, VK_RWIN
        _ => "Unknown"
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopCapture();
    }
}
