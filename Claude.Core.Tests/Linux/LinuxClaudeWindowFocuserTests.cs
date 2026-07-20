using Claude.Core.Linux;
using Claude.Core.Models;

namespace Claude.Core.Tests.Linux;

[TestClass]
public class LinuxClaudeWindowFocuserTests
{
    sealed class FakeProc : IProcFileSystem
    {
        public long BootTimeEpochSeconds => 0;
        public long ClockTicksPerSecond => 100;
        public Dictionary<int, int?> Parents = new();
        public IEnumerable<int> EnumeratePids() => Parents.Keys;
        public string? ReadComm(int pid) => null;
        public string? ReadCwd(int pid) => null;
        public long? ReadStartTicks(int pid) => null;
        public string? ReadEnviron(int pid) => null;
        public int? ReadParentPid(int pid) => Parents.TryGetValue(pid, out var parent) ? parent : null;
    }

    [TestMethod]
    public void AncestorChain_WalksToInit_NearestFirst()
    {
        static int? ParentOf(int pid) => pid switch { 100 => 50, 50 => 7, 7 => 1, _ => null };

        var chain = LinuxClaudeWindowFocuser.AncestorChain(100, ParentOf, maximumDepth: 32);

        Assert.AreEqual("100,50,7", string.Join(',', chain));
    }

    [TestMethod]
    public void AncestorChain_ParentCycle_Terminates()
    {
        static int? ParentOf(int pid) => pid switch { 100 => 50, 50 => 100, _ => null };

        var chain = LinuxClaudeWindowFocuser.AncestorChain(100, ParentOf, maximumDepth: 32);

        Assert.AreEqual("100,50", string.Join(',', chain));
    }

    [TestMethod]
    public void AncestorChain_DepthCap_Respected()
    {
        var chain = LinuxClaudeWindowFocuser.AncestorChain(1000, pid => pid + 1, maximumDepth: 3);

        Assert.HasCount(3, chain);
    }

    [TestMethod]
    public void BuildActivationScript_EmbedsPidsInOrder()
    {
        var script = LinuxClaudeWindowFocuser.BuildActivationScript([300, 200, 100]);

        StringAssert.Contains(script, "var pids = [300, 200, 100];");
        StringAssert.Contains(script, "workspace.activeWindow = windows[i];");
    }

    [TestMethod]
    public void BringToFront_ZeroToken_ReturnsFalse_WithoutQdbus()
    {
        var calls = 0;
        var sut = new LinuxClaudeWindowFocuser(new FakeProc(), _ => { calls++; return ""; });

        Assert.IsFalse(sut.BringToFront(WindowToken.Null));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void BringToFront_LoadRunUnload_SequenceAndSuccess()
    {
        var fake = new FakeProc { Parents = { [100] = 50, [50] = 1 } };
        var invocations = new List<string>();
        var sut = new LinuxClaudeWindowFocuser(fake, arguments =>
        {
            invocations.Add(string.Join(' ', arguments[2..]));
            return arguments[2].EndsWith(".loadScript", StringComparison.Ordinal) ? "3" : "";
        });

        Assert.IsTrue(sut.BringToFront(WindowToken.FromHandle(100)));

        Assert.HasCount(4, invocations);
        StringAssert.Contains(invocations[0], "unloadScript");
        StringAssert.Contains(invocations[1], "loadScript");
        Assert.AreEqual("org.kde.kwin.Script.run", invocations[2]);
        StringAssert.Contains(invocations[3], "unloadScript");
    }

    [TestMethod]
    public void BringToFront_LoadScriptFails_ReturnsFalse()
    {
        var sut = new LinuxClaudeWindowFocuser(new FakeProc(), arguments =>
            arguments[2].EndsWith(".loadScript", StringComparison.Ordinal) ? null : "");

        Assert.IsFalse(sut.BringToFront(WindowToken.FromHandle(100)));
    }
}
