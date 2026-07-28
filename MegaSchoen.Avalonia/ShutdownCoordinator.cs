namespace MegaSchoen.Avalonia;

sealed class ShutdownCoordinator
{
    volatile bool _isShuttingDown;

    public bool ShouldHideWindow => !_isShuttingDown;

    public void RequestShutdown()
    {
        _isShuttingDown = true;
    }
}
