namespace MegaSchoen.Platforms.Windows.Services;

/// <summary>
/// Ensures only a single instance of the application runs at a time.
/// Uses a named mutex for cross-process synchronization.
/// </summary>
sealed class SingleInstanceService : IDisposable
{
    const string MutexName = "Global\\MegaSchoen_SingleInstance";

    Mutex? _mutex;
    bool _hasHandle;
    bool _disposed;

    /// <summary>
    /// Attempts to acquire the single-instance lock.
    /// </summary>
    /// <returns>True if this is the first instance, false if another instance is running.</returns>
    public bool TryAcquire()
    {
        _mutex = new Mutex(false, MutexName, out var createdNew);

        try
        {
            _hasHandle = _mutex.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            // Previous instance crashed without releasing mutex
            _hasHandle = true;
        }

        if (!_hasHandle)
        {
            // Another instance is running - signal it to activate
            MessageWindow.SignalExistingInstance();
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_mutex != null)
        {
            if (_hasHandle)
            {
                _mutex.ReleaseMutex();
            }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
