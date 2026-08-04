using System.Runtime.InteropServices;

namespace DisplayManager.Core;

readonly record struct SystemWakeTimeQueryResult(bool Success, ulong Timestamp);

sealed class WindowsSystemWakeTimeProvider : ISystemWakeTimeProvider
{
    // LastWakeTime uses the same sleep-inclusive uptime clock as GetTickCount64,
    // but reports 100-nanosecond units instead of milliseconds.
    const int LastWakeTimeInformationLevel = 14;
    const int StatusSuccess = 0;
    readonly Func<SystemWakeTimeQueryResult> _lastWakeTimeQuery;
    readonly Func<ulong> _uptimeMillisecondsQuery;

    public WindowsSystemWakeTimeProvider()
        : this(QueryLastWakeTime, GetTickCount64)
    {
    }

    internal WindowsSystemWakeTimeProvider(
        Func<SystemWakeTimeQueryResult> lastWakeTimeQuery,
        Func<ulong> uptimeMillisecondsQuery)
    {
        _lastWakeTimeQuery = lastWakeTimeQuery;
        _uptimeMillisecondsQuery = uptimeMillisecondsQuery;
    }

    public TimeSpan? GetElapsedTimeSinceWake()
    {
        var lastWakeTime = _lastWakeTimeQuery();
        if (!lastWakeTime.Success)
        {
            return null;
        }

        var uptimeMilliseconds = _uptimeMillisecondsQuery();
        if (uptimeMilliseconds > ulong.MaxValue / TimeSpan.TicksPerMillisecond)
        {
            return null;
        }

        var currentTime = uptimeMilliseconds * TimeSpan.TicksPerMillisecond;
        if (currentTime < lastWakeTime.Timestamp)
        {
            return null;
        }

        var elapsedTicks = currentTime - lastWakeTime.Timestamp;
        return elapsedTicks <= long.MaxValue
            ? TimeSpan.FromTicks((long)elapsedTicks)
            : null;
    }

    static SystemWakeTimeQueryResult QueryLastWakeTime()
    {
        var status = CallNtPowerInformation(
            LastWakeTimeInformationLevel,
            nint.Zero,
            0,
            out var timestamp,
            sizeof(ulong));
        return new SystemWakeTimeQueryResult(status == StatusSuccess, timestamp);
    }

    [DllImport("PowrProf.dll")]
    static extern int CallNtPowerInformation(
        int informationLevel,
        nint inputBuffer,
        uint inputBufferLength,
        out ulong outputBuffer,
        uint outputBufferLength);

    [DllImport("Kernel32.dll")]
    static extern ulong GetTickCount64();
}
