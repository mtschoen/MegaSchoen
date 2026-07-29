using System;
using System.Threading;

namespace MegaSchoen.Avalonia;

sealed class WindowsSingleInstanceLock : ISingleInstanceLock
{
    const string MutexName = @"Local\MegaSchoen.Avalonia.SingleInstance";

    readonly Mutex _mutex = new(false, MutexName);
    bool _attempted;
    bool _hasHandle;
    bool _disposed;

    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_attempted)
        {
            return _hasHandle;
        }

        _attempted = true;
        try
        {
            _hasHandle = _mutex.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            _hasHandle = true;
        }

        return _hasHandle;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hasHandle)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
