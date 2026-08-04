using System.Globalization;
using DisplayManager.Core.Models;

namespace DisplayManager.Core.Tests;

[TestClass]
public class DisplayApplyCooldownTests
{
    readonly List<string> _statePaths = [];

    [TestMethod]
    public void DefaultSharedStatePath_UsesMegaSchoenLocalApplicationDataDirectory()
    {
        var expectedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MegaSchoen");

        Assert.AreEqual(
            Path.Combine(expectedDirectory, "display-resume.timestamp"),
            DisplayApplyCooldown.DefaultSharedStatePath);
    }

    [TestCleanup]
    public void CleanupStateFiles()
    {
        foreach (var statePath in _statePaths)
        {
            File.Delete(statePath);
        }
    }

    [TestMethod]
    public void GetRemainingCooldown_BeforeResume_ReturnsZero()
    {
        var timeProvider = new ManualTimeProvider();
        var cooldown = CreateCooldown(timeProvider);

        var remaining = cooldown.GetRemainingCooldown();

        Assert.AreEqual(TimeSpan.Zero, remaining);
    }

    [TestMethod]
    public void GetRemainingCooldown_FourSecondsAfterSystemWake_ReturnsElevenSeconds()
    {
        var timeProvider = new ManualTimeProvider();
        var cooldown = CreateCooldown(
            timeProvider,
            new FixedSystemWakeTimeProvider(TimeSpan.FromSeconds(4)));

        Assert.AreEqual(TimeSpan.FromSeconds(11), cooldown.GetRemainingCooldown());
    }

    [TestMethod]
    public void GetRemainingCooldown_SystemWakeBeforeCooldown_ReturnsZero()
    {
        var timeProvider = new ManualTimeProvider();
        var cooldown = CreateCooldown(
            timeProvider,
            new FixedSystemWakeTimeProvider(TimeSpan.FromSeconds(15)));

        Assert.AreEqual(TimeSpan.Zero, cooldown.GetRemainingCooldown());
    }

    [TestMethod]
    public void GetRemainingCooldown_InvalidNegativeSystemWakeElapsedTime_ReturnsZero()
    {
        var timeProvider = new ManualTimeProvider();
        var cooldown = CreateCooldown(
            timeProvider,
            new FixedSystemWakeTimeProvider(TimeSpan.FromSeconds(-1)));

        Assert.AreEqual(TimeSpan.Zero, cooldown.GetRemainingCooldown());
    }

    [TestMethod]
    public void GetRemainingCooldown_SharedResumeIsMoreRecentThanSystemWake_ReturnsSharedCooldown()
    {
        var timeProvider = new ManualTimeProvider();
        var cooldown = CreateCooldown(
            timeProvider,
            new FixedSystemWakeTimeProvider(TimeSpan.FromSeconds(8)));
        cooldown.RecordResume();
        timeProvider.Advance(TimeSpan.FromSeconds(3));

        Assert.AreEqual(TimeSpan.FromSeconds(12), cooldown.GetRemainingCooldown());
    }

    [TestMethod]
    public void Constructor_RootPath_ThrowsArgumentException()
    {
        var timeProvider = new ManualTimeProvider();

        Assert.Throws<ArgumentException>(() =>
            new DisplayApplyCooldown(
                timeProvider,
                TimeSpan.FromSeconds(15),
                Path.DirectorySeparatorChar.ToString(),
                NullSystemWakeTimeProvider.Instance));
    }

    [TestMethod]
    public void GetRemainingCooldown_DuringCooldown_ReturnsRemainingTime()
    {
        var timeProvider = new ManualTimeProvider();
        var cooldown = CreateCooldown(timeProvider);

        cooldown.RecordResume();
        timeProvider.Advance(TimeSpan.FromSeconds(4));

        Assert.AreEqual(TimeSpan.FromSeconds(11), cooldown.GetRemainingCooldown());
    }

    [TestMethod]
    public void GetRemainingCooldown_AfterCooldown_ReturnsZero()
    {
        var timeProvider = new ManualTimeProvider();
        var cooldown = CreateCooldown(timeProvider);

        cooldown.RecordResume();
        timeProvider.Advance(TimeSpan.FromSeconds(15));

        Assert.AreEqual(TimeSpan.Zero, cooldown.GetRemainingCooldown());
    }

    [TestMethod]
    public void RecordResume_DuringCooldown_RestartsCooldown()
    {
        var timeProvider = new ManualTimeProvider();
        var cooldown = CreateCooldown(timeProvider);

        cooldown.RecordResume();
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        cooldown.RecordResume();
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        Assert.AreEqual(TimeSpan.FromSeconds(13), cooldown.GetRemainingCooldown());
    }

    [TestMethod]
    public void GetRemainingCooldown_ResumeRecordedByAnotherInstance_ReturnsRemainingTime()
    {
        var timeProvider = new ManualTimeProvider();
        var statePath = CreateStatePath();
        var source = CreateCooldown(timeProvider, statePath);
        var observer = CreateCooldown(timeProvider, statePath);

        try
        {
            source.RecordResume();
            timeProvider.Advance(TimeSpan.FromSeconds(4));

            Assert.AreEqual(TimeSpan.FromSeconds(11), observer.GetRemainingCooldown());
        }
        finally
        {
            File.Delete(statePath);
        }
    }

    [TestMethod]
    public void GetRemainingCooldown_AfterWallClockCorrection_UsesMonotonicElapsedTime()
    {
        var timeProvider = new ManualTimeProvider();
        var statePath = CreateStatePath();
        var source = CreateCooldown(timeProvider, statePath);
        var observer = CreateCooldown(timeProvider, statePath);

        source.RecordResume();
        timeProvider.Advance(TimeSpan.FromSeconds(4));
        timeProvider.AdjustUtc(TimeSpan.FromMinutes(-1));

        Assert.AreEqual(TimeSpan.FromSeconds(11), observer.GetRemainingCooldown());
    }

    [TestMethod]
    public void RecordResume_WhileSharedStateMutexIsOwned_WaitsForMutex()
    {
        const string mutexName = "MegaSchoen.DisplayApplyCooldown";
        using var mutex = new Mutex(false, mutexName);
        using var started = new ManualResetEventSlim();
        var timeProvider = new ManualTimeProvider();
        var cooldown = CreateCooldown(timeProvider);
        mutex.WaitOne();

        var recordTask = Task.Run(() =>
        {
            started.Set();
            cooldown.RecordResume();
        });

        try
        {
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(1)));
            Assert.IsFalse(recordTask.Wait(TimeSpan.FromMilliseconds(100)));
        }
        finally
        {
            mutex.ReleaseMutex();
        }

        Assert.IsTrue(recordTask.Wait(TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void RecordResume_AfterStateMutexOwnerExits_RecoversAbandonedMutex()
    {
        const string mutexName = "MegaSchoen.DisplayApplyCooldown";
        using var keeper = new Mutex(false, mutexName);
        var ownerThread = new Thread(() =>
        {
            using var owner = new Mutex(false, mutexName);
            owner.WaitOne();
        });
        ownerThread.Start();
        Assert.IsTrue(ownerThread.Join(TimeSpan.FromSeconds(1)));

        var messages = new List<string>();
        DiagnosticLog.Sink = messages.Add;
        var timeProvider = new ManualTimeProvider();
        var cooldown = CreateCooldown(timeProvider);

        try
        {
            cooldown.RecordResume();

            Assert.AreEqual(TimeSpan.FromSeconds(15), cooldown.GetRemainingCooldown());
            Assert.IsTrue(messages.Any(message => message.Contains("abandoned", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DiagnosticLog.Sink = null;
        }
    }

    [TestMethod]
    public void GetRemainingCooldown_MalformedSharedTimestamp_ReturnsZero()
    {
        var timeProvider = new ManualTimeProvider();
        var statePath = CreateStatePath();
        File.WriteAllText(statePath, "not-a-timestamp");
        var cooldown = CreateCooldown(timeProvider, statePath);

        try
        {
            Assert.AreEqual(TimeSpan.Zero, cooldown.GetRemainingCooldown());
        }
        finally
        {
            File.Delete(statePath);
        }
    }

    [TestMethod]
    public void GetRemainingCooldown_FarFutureSharedTimestamp_ReturnsZero()
    {
        var timeProvider = new ManualTimeProvider();
        var statePath = CreateStatePath();
        var future = long.MaxValue;
        File.WriteAllText(statePath, future.ToString(CultureInfo.InvariantCulture));
        var cooldown = CreateCooldown(timeProvider, statePath);

        try
        {
            Assert.AreEqual(TimeSpan.Zero, cooldown.GetRemainingCooldown());
        }
        finally
        {
            File.Delete(statePath);
        }
    }

    [TestMethod]
    public void GetRemainingCooldown_CurrentSharedTimestamp_ReturnsFullCooldown()
    {
        var timeProvider = new ManualTimeProvider();
        var statePath = CreateStatePath();
        File.WriteAllText(statePath, timeProvider.GetTimestamp().ToString(CultureInfo.InvariantCulture));
        var cooldown = CreateCooldown(timeProvider, statePath);

        Assert.AreEqual(TimeSpan.FromSeconds(15), cooldown.GetRemainingCooldown());
    }

    [TestMethod]
    public void GetRemainingCooldown_UnreadableSharedTimestamp_ReturnsZeroAndLogs()
    {
        var messages = new List<string>();
        DiagnosticLog.Sink = messages.Add;
        var timeProvider = new ManualTimeProvider();
        var statePath = CreateStatePath();
        Directory.CreateDirectory(statePath);
        var cooldown = CreateCooldown(timeProvider, statePath);

        try
        {
            Assert.AreEqual(TimeSpan.Zero, cooldown.GetRemainingCooldown());
            Assert.HasCount(1, messages);
        }
        finally
        {
            DiagnosticLog.Sink = null;
            Directory.Delete(statePath);
            _statePaths.Remove(statePath);
        }
    }

    [TestMethod]
    public void RecordResume_WhenSharedTimestampCannotBeWritten_RetainsLocalCooldownAndLogs()
    {
        var messages = new List<string>();
        DiagnosticLog.Sink = messages.Add;
        var timeProvider = new ManualTimeProvider();
        var blockingFile = CreateStatePath();
        File.WriteAllText(blockingFile, "not a directory");
        var cooldown = new DisplayApplyCooldown(
            timeProvider,
            TimeSpan.FromSeconds(15),
            Path.Combine(blockingFile, "display-resume.timestamp"),
            NullSystemWakeTimeProvider.Instance);

        try
        {
            cooldown.RecordResume();

            Assert.AreEqual(TimeSpan.FromSeconds(15), cooldown.GetRemainingCooldown());
            Assert.HasCount(1, messages);
        }
        finally
        {
            DiagnosticLog.Sink = null;
        }
    }

    [TestMethod]
    public void ApplyConfiguration_ImmediatelyAfterResume_IsDeferredBeforeNativeCall()
    {
        var messages = new List<string>();
        DiagnosticLog.Sink = messages.Add;
        var timeProvider = new ManualTimeProvider();
        var cooldown = CreateCooldown(timeProvider);
        var nativeCalled = false;

        try
        {
            cooldown.RecordResume();

            var result = DisplayManager.ApplyConfiguration(
                Array.Empty<SavedDisplayConfig>(),
                cooldown,
                _ =>
                {
                    nativeCalled = true;
                    return 0;
                });

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Deferred);
            Assert.IsGreaterThan(TimeSpan.Zero, result.RetryAfter);
            Assert.Contains("system resume", result.Errors.Single());
            Assert.IsFalse(nativeCalled);
            Assert.IsTrue(messages.Any(message => message.Contains("deferred", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DiagnosticLog.Sink = null;
        }
    }

    DisplayApplyCooldown CreateCooldown(TimeProvider timeProvider) =>
        CreateCooldown(timeProvider, CreateStatePath());

    static DisplayApplyCooldown CreateCooldown(TimeProvider timeProvider, string statePath) =>
        new(
            timeProvider,
            TimeSpan.FromSeconds(15),
            statePath,
            NullSystemWakeTimeProvider.Instance);

    DisplayApplyCooldown CreateCooldown(
        TimeProvider timeProvider,
        ISystemWakeTimeProvider systemWakeTimeProvider) =>
        new(timeProvider, TimeSpan.FromSeconds(15), CreateStatePath(), systemWakeTimeProvider);

    string CreateStatePath()
    {
        var statePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.resume");
        _statePaths.Add(statePath);
        return statePath;
    }

    sealed class ManualTimeProvider : TimeProvider
    {
        DateTimeOffset _utcNow = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
        long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration)
        {
            _timestamp += duration.Ticks;
            _utcNow += duration;
        }

        public void AdjustUtc(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }

    sealed class FixedSystemWakeTimeProvider(TimeSpan? elapsedTimeSinceWake) : ISystemWakeTimeProvider
    {
        public TimeSpan? GetElapsedTimeSinceWake() => elapsedTimeSinceWake;
    }
}
