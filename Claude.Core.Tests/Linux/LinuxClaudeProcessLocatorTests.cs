using Claude.Core.Linux;
using Claude.Core.Models;

namespace Claude.Core.Tests.Linux;

[TestClass]
public class LinuxClaudeProcessLocatorTests
{
    sealed class FakeProc : IProcFileSystem
    {
        public long BootTimeEpochSeconds => 1_000_000;
        public long ClockTicksPerSecond => 100;
        public Dictionary<int, (string comm, string? cwd, long ticks)> Procs = new();
        public IEnumerable<int> EnumeratePids() => Procs.Keys;
        public string? ReadComm(int pid) => Procs.TryGetValue(pid, out var p) ? p.comm : null;
        public string? ReadCwd(int pid) => Procs.TryGetValue(pid, out var p) ? p.cwd : null;
        public long? ReadStartTicks(int pid) => Procs.TryGetValue(pid, out var p) ? p.ticks : null;
    }

    [TestMethod]
    public void EnumerateWindows_ReturnsOnlyCommClaude_WithCwdAndStartTime()
    {
        var fake = new FakeProc();
        fake.Procs[2572000] = ("claude", "/home/schoen/pr-crew", 200);          // start = 1_000_000 + 2s
        fake.Procs[2680058] = ("bash", "/home/schoen/git-wizard", 300);          // child shell — must be excluded
        fake.Procs[999]     = ("claude", null, 100);                             // no cwd — must be excluded

        var sut = new LinuxClaudeProcessLocator(fake);
        var windows = sut.EnumerateWindows();

        Assert.AreEqual(1, windows.Count);
        var w = windows[0];
        Assert.AreEqual("/home/schoen/pr-crew", w.WorkingDirectory);
        Assert.AreEqual((uint)2572000, w.ProcessId);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1_000_002), w.StartTimeUtc);
        Assert.IsTrue(w.Window.IsZero);   // no window on the remote (scope A)
    }
}
