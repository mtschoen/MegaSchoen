using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static MegaSchoen.Avalonia.WindowsNativeMethods;

namespace MegaSchoen.Avalonia;

[SupportedOSPlatform("windows")]
sealed class WindowsMessageWindow : IHotkeyRegistrar, IDisposable
{
    const string WindowClassName = "MegaSchoen.Avalonia.MessageWindow";
    const string ActivationMessageName = "MegaSchoen.Avalonia.Activate";
    const int ErrorClassAlreadyExists = 1410;

    readonly WindowProcedure _windowProcedure;
    IntPtr _window;
    bool _disposed;

    public WindowsMessageWindow()
    {
        _windowProcedure = OnWindowMessage;
        ActivationMessage = RegisterWindowMessage(ActivationMessageName);
        Create();
    }

    public event EventHandler<int>? Pressed;

    public event EventHandler? ActivationRequested;

    uint ActivationMessage { get; }

    public bool Register(int id, uint modifiers, uint virtualKey) =>
        RegisterHotKey(_window, id, modifiers, virtualKey);

    public void Unregister(int id)
    {
        UnregisterHotKey(_window, id);
    }

    public static void SignalExistingInstance()
    {
        var window = FindWindow(WindowClassName, null);
        if (window == IntPtr.Zero)
        {
            return;
        }

        var message = RegisterWindowMessage(ActivationMessageName);
        PostMessage(window, message, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_window != IntPtr.Zero)
        {
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }
    }

    void Create()
    {
        var instance = GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            Size = checked((uint)Marshal.SizeOf<WindowClass>()),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            Instance = instance,
            ClassName = WindowClassName
        };
        if (RegisterClassEx(ref windowClass) == 0
            && Marshal.GetLastPInvokeError() != ErrorClassAlreadyExists)
        {
            throw new InvalidOperationException(
                $"Could not register the hotkey window class (Win32 error {Marshal.GetLastPInvokeError()}).");
        }

        _window = CreateWindowEx(
            0,
            WindowClassName,
            "MegaSchoen Avalonia Message Window",
            0,
            0,
            0,
            0,
            0,
            MessageOnlyWindow,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);
        if (_window == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Could not create the hotkey message window (Win32 error {Marshal.GetLastPInvokeError()}).");
        }
    }

    IntPtr OnWindowMessage(IntPtr window, uint message, IntPtr word, IntPtr longWord)
    {
        if (message == WmHotkey)
        {
            Pressed?.Invoke(this, checked((int)word));
            return IntPtr.Zero;
        }

        if (message == ActivationMessage)
        {
            ActivationRequested?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        return DefWindowProc(window, message, word, longWord);
    }
}
