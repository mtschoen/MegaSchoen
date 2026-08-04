using System.Globalization;

namespace DisplayManager.Core;

/// <summary>
/// Tracks the interval after a system resume during which display mode sets are unsafe.
/// Combines the operating system's last-wake time with in-process and shared monotonic timestamps.
/// </summary>
public sealed class DisplayApplyCooldown
{
    readonly TimeProvider _timeProvider;
    readonly string _sharedStateDirectory;
    readonly string _sharedStatePath;
    readonly ISystemWakeTimeProvider _systemWakeTimeProvider;
    long _lastResumeTimestamp;
    int _hasResumed;

    public DisplayApplyCooldown(TimeProvider timeProvider, TimeSpan cooldown, string sharedStatePath)
        : this(
            timeProvider,
            cooldown,
            sharedStatePath,
            OperatingSystem.IsWindows()
                ? new WindowsSystemWakeTimeProvider()
                : NullSystemWakeTimeProvider.Instance)
    {
    }

    internal DisplayApplyCooldown(
        TimeProvider timeProvider,
        TimeSpan cooldown,
        string sharedStatePath,
        ISystemWakeTimeProvider systemWakeTimeProvider)
    {
        _timeProvider = timeProvider;
        _systemWakeTimeProvider = systemWakeTimeProvider;
        Cooldown = cooldown;
        _sharedStatePath = Path.GetFullPath(sharedStatePath);
        _sharedStateDirectory = Path.GetDirectoryName(_sharedStatePath)
            ?? throw new ArgumentException("Shared state path must include a file name.", nameof(sharedStatePath));
    }

    public static string DefaultSharedStatePath =>
        Path.Combine(DisplayProfileDataPaths.LocalStateDirectory, "display-resume.timestamp");

    public TimeSpan Cooldown { get; }

    public void RecordResume()
    {
        Interlocked.Exchange(ref _lastResumeTimestamp, _timeProvider.GetTimestamp());
        Volatile.Write(ref _hasResumed, 1);

        try
        {
            PersistSharedResumeTimestamp();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Log($"DisplayApplyCooldown.RecordResume({_sharedStatePath}): {exception.Message}");
        }
    }

    public TimeSpan GetRemainingCooldown()
    {
        var localRemaining = GetLocalRemainingCooldown();
        var sharedRemaining = GetSharedRemainingCooldown();
        var systemRemaining = GetSystemRemainingCooldown();
        if (sharedRemaining > TimeSpan.Zero)
        {
            DiagnosticLog.Log("DisplayApplyCooldown: shared resume timestamp is active.");
        }

        return Max(localRemaining, sharedRemaining, systemRemaining);
    }

    TimeSpan GetSystemRemainingCooldown()
    {
        var elapsedTimeSinceWake = _systemWakeTimeProvider.GetElapsedTimeSinceWake();
        if (elapsedTimeSinceWake is null || elapsedTimeSinceWake < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var remaining = Cooldown - elapsedTimeSinceWake.Value;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    static TimeSpan Max(TimeSpan first, TimeSpan second, TimeSpan third) =>
        first > second
            ? first > third ? first : third
            : second > third ? second : third;

    TimeSpan GetLocalRemainingCooldown()
    {
        if (Volatile.Read(ref _hasResumed) == 0)
        {
            return TimeSpan.Zero;
        }

        var resumeTimestamp = Interlocked.Read(ref _lastResumeTimestamp);
        var elapsed = _timeProvider.GetElapsedTime(resumeTimestamp, _timeProvider.GetTimestamp());
        var remaining = Cooldown - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    TimeSpan GetSharedRemainingCooldown()
    {
        try
        {
            using var stateMutex = AcquireSharedStateMutex();
            try
            {
                return ReadSharedRemainingCooldown();
            }
            finally
            {
                stateMutex.ReleaseMutex();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Log($"DisplayApplyCooldown.GetRemainingCooldown({_sharedStatePath}): {exception.Message}");
            return TimeSpan.Zero;
        }
    }

    TimeSpan ReadSharedRemainingCooldown()
    {
        if (!Path.Exists(_sharedStatePath))
        {
            return TimeSpan.Zero;
        }

        var contents = File.ReadAllText(_sharedStatePath);
        if (!long.TryParse(contents, NumberStyles.Integer, CultureInfo.InvariantCulture, out var resumeTimestamp))
        {
            return TimeSpan.Zero;
        }

        var currentTimestamp = _timeProvider.GetTimestamp();
        if (resumeTimestamp > currentTimestamp)
        {
            return TimeSpan.Zero;
        }

        var elapsed = _timeProvider.GetElapsedTime(resumeTimestamp, currentTimestamp);
        var remaining = Cooldown - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    void PersistSharedResumeTimestamp()
    {
        using var stateMutex = AcquireSharedStateMutex();
        try
        {
            Directory.CreateDirectory(_sharedStateDirectory);

            var tempPath = $"{_sharedStatePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            try
            {
                var timestamp = _timeProvider.GetTimestamp();
                File.WriteAllText(tempPath, timestamp.ToString(CultureInfo.InvariantCulture));
                File.Move(tempPath, _sharedStatePath, overwrite: true);
            }
            finally
            {
                File.Delete(tempPath);
            }
        }
        finally
        {
            stateMutex.ReleaseMutex();
        }
    }

    Mutex AcquireSharedStateMutex()
    {
        var stateMutex = new Mutex(false, "MegaSchoen.DisplayApplyCooldown");
        try
        {
            stateMutex.WaitOne();
        }
        catch (AbandonedMutexException exception)
        {
            DiagnosticLog.Log($"DisplayApplyCooldown({_sharedStatePath}): recovered abandoned state mutex: {exception.Message}");
        }

        return stateMutex;
    }
}
