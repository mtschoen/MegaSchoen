namespace DisplayManager.Core;

interface ISystemWakeTimeProvider
{
    TimeSpan? GetElapsedTimeSinceWake();
}

sealed class NullSystemWakeTimeProvider : ISystemWakeTimeProvider
{
    NullSystemWakeTimeProvider()
    {
    }

    public static NullSystemWakeTimeProvider Instance { get; } = new();

    public TimeSpan? GetElapsedTimeSinceWake() => null;
}
