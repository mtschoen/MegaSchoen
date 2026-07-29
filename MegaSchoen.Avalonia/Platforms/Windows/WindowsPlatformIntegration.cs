using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Claude.Core;

namespace MegaSchoen.Avalonia;

[SupportedOSPlatform("windows")]
sealed class WindowsPlatformIntegration : IDisposable
{
    readonly WindowsMessageWindow _messageWindow;
    readonly HotkeyService _hotkeys;
    readonly DisplayHotkeyCoordinator _displayHotkeys;
    readonly EventHandler _activationHandler;
    bool _disposed;

    public WindowsPlatformIntegration(Action activate)
    {
        DisplayManager.Core.DiagnosticLog.Sink = Logger.Log;

        _messageWindow = new WindowsMessageWindow();
        _hotkeys = new HotkeyService(_messageWindow);
        _displayHotkeys = new DisplayHotkeyCoordinator(
            _hotkeys,
            new DisplayProfileActions(),
            Logger.Log);
        _activationHandler = (_, _) => activate();
        _messageWindow.ActivationRequested += _activationHandler;
    }

    public Task StartAsync()
    {
        return _displayHotkeys.StartAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messageWindow.ActivationRequested -= _activationHandler;
        _displayHotkeys.Dispose();
        _hotkeys.Dispose();
        _messageWindow.Dispose();
    }
}
