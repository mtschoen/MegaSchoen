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
        public Dictionary<int, string?> Environs = new();
        public IEnumerable<int> EnumeratePids() => Procs.Keys;
        public string? ReadComm(int pid) => Procs.TryGetValue(pid, out var p) ? p.comm : null;
        public string? ReadCwd(int pid) => Procs.TryGetValue(pid, out var p) ? p.cwd : null;
        public long? ReadStartTicks(int pid) => Procs.TryGetValue(pid, out var p) ? p.ticks : null;
        public string? ReadEnviron(int pid) => Environs.TryGetValue(pid, out var e) ? e : null;
    }

    [TestMethod]
    public void EnumerateWindows_ReturnsOnlyCommClaude_WithCwdAndStartTime()
    {
        var fake = new FakeProc();
        fake.Procs[2572000] = ("claude", "/home/schoen/pr-crew", 200);          // start = 1_000_000 + 2s
        fake.Procs[2680058] = ("bash", "/home/schoen/git-wizard", 300);          // child shell — must be excluded
        fake.Procs[999] = ("claude", null, 100);                             // no cwd — must be excluded

        var sut = new LinuxClaudeProcessLocator(fake);
        var windows = sut.EnumerateLiveSessions();

        Assert.HasCount(1, windows);
        var w = windows[0];
        Assert.AreEqual("/home/schoen/pr-crew", w.WorkingDirectory);
        Assert.AreEqual((uint)2572000, w.ProcessId);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1_000_002), w.StartTimeUtc);
        Assert.IsTrue(w.Window.IsZero);   // no window on the remote (scope A)
    }

    [TestMethod]
    public void EnumerateWindows_PopulatesSshClientPort_FromEnviron()
    {
        var fake = new FakeProc();
        fake.Procs[4242] = ("claude", "/home/schoen/site", 200);
        fake.Environs[4242] = "PATH=/usr/bin\0SSH_CONNECTION=10.0.0.5 51000 10.0.0.9 22\0";

        var windows = new LinuxClaudeProcessLocator(fake).EnumerateLiveSessions();

        Assert.HasCount(1, windows);
        Assert.AreEqual(51000, windows[0].SshClientPort);
    }

    [TestMethod]
    public void EnumerateWindows_NoSshConnection_LeavesPortNull()
    {
        var fake = new FakeProc();
        fake.Procs[4243] = ("claude", "/home/schoen/site", 200);
        fake.Environs[4243] = "PATH=/usr/bin\0HOME=/home/schoen\0";

        var windows = new LinuxClaudeProcessLocator(fake).EnumerateLiveSessions();

        Assert.HasCount(1, windows);
        Assert.IsNull(windows[0].SshClientPort);
    }
}
