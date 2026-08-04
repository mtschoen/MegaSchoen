namespace DisplayManager.Core.Tests;

[TestClass]
public class WindowsSystemWakeTimeProviderTests
{
    [TestMethod]
    public void GetElapsedTimeSinceWake_SuccessfulQueries_ConvertsUptimeMilliseconds()
    {
        var provider = new WindowsSystemWakeTimeProvider(
            () => new SystemWakeTimeQueryResult(true, 1_000_000),
            () => 4_100);

        Assert.AreEqual(TimeSpan.FromSeconds(4), provider.GetElapsedTimeSinceWake());
    }

    [TestMethod]
    public void GetElapsedTimeSinceWake_LastWakeQueryFails_DoesNotQueryCurrentTime()
    {
        var currentTimeQueried = false;
        var provider = new WindowsSystemWakeTimeProvider(
            () => new SystemWakeTimeQueryResult(false, 0),
            () =>
            {
                currentTimeQueried = true;
                return 0;
            });

        Assert.IsNull(provider.GetElapsedTimeSinceWake());
        Assert.IsFalse(currentTimeQueried);
    }

    [TestMethod]
    public void GetElapsedTimeSinceWake_LastWakeIsAfterCurrentTime_ReturnsNull()
    {
        var provider = new WindowsSystemWakeTimeProvider(
            () => new SystemWakeTimeQueryResult(true, 2_000_000),
            () => 100);

        Assert.IsNull(provider.GetElapsedTimeSinceWake());
    }

    [TestMethod]
    public void GetElapsedTimeSinceWake_DifferenceExceedsTimeSpan_ReturnsNull()
    {
        var provider = new WindowsSystemWakeTimeProvider(
            () => new SystemWakeTimeQueryResult(true, 0),
            () => ((ulong)long.MaxValue / TimeSpan.TicksPerMillisecond) + 1);

        Assert.IsNull(provider.GetElapsedTimeSinceWake());
    }

    [TestMethod]
    public void GetElapsedTimeSinceWake_UptimeMillisecondsOverflowInterruptTime_ReturnsNull()
    {
        var provider = new WindowsSystemWakeTimeProvider(
            () => new SystemWakeTimeQueryResult(true, 0),
            () => ulong.MaxValue);

        Assert.IsNull(provider.GetElapsedTimeSinceWake());
    }
}
