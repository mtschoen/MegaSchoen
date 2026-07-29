using System;

namespace MegaSchoen.Avalonia;

sealed class SingleInstanceGuard : IDisposable
{
    readonly ISingleInstanceLock _instanceLock;
    readonly Action _signalPrimary;

    public SingleInstanceGuard(ISingleInstanceLock instanceLock, Action signalPrimary)
    {
        _instanceLock = instanceLock;
        _signalPrimary = signalPrimary;
    }

    public bool TryAcquire()
    {
        if (_instanceLock.TryAcquire())
        {
            return true;
        }

        _signalPrimary();
        return false;
    }

    public void Dispose()
    {
        _instanceLock.Dispose();
    }
}

interface ISingleInstanceLock : IDisposable
{
    bool TryAcquire();
}
