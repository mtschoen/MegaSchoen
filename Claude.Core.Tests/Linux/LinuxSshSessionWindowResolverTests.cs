using Claude.Core.Linux;
using Claude.Core.Models;

namespace Claude.Core.Tests.Linux;

[TestClass]
public class LinuxSshSessionWindowResolverTests
{
    // Real /proc/net/tcp fixture line: local port 0x9C40 = 40000, ESTABLISHED, inode 55001.
    const string TcpFixture =
        "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\n" +
        "   0: 0100007F:9C40 0100007F:0016 01 00000000:00000000 00:00000000 00000000  1000        0 55001 1 0000000000000000 20 4 0 10 -1\n" +
        "   1: 0100007F:0050 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 55002 1 0000000000000000 20 4 0 10 -1\n";

    // Same connection over IPv6 (32-hex-char address, still colon-separated hex port).
    const string Tcp6Fixture =
        "  sl  local_address                         remote_address                        st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\n" +
        "   0: 00000000000000000000000001000000:9C40 00000000000000000000000001000000:0016 01 00000000:00000000 00:00000000 00000000  1000        0 66001 1 0000000000000000 20 4 0 10 -1\n";

    sealed class FakeProc : IProcFileSystem
    {
        public string? NetTcp;
        public string? NetTcp6;
        public Dictionary<long, int> InodeOwners = new();
        public Dictionary<int, string?> Comms = new();
        public Dictionary<int, string?> Cmdlines = new();

        public long BootTimeEpochSeconds => 0;
        public long ClockTicksPerSecond => 100;
        public IEnumerable<int> EnumeratePids() => Comms.Keys;
        public string? ReadComm(int pid) => Comms.TryGetValue(pid, out var comm) ? comm : null;
        public string? ReadCwd(int pid) => null;
        public long? ReadStartTicks(int pid) => null;
        public int? ReadParentPid(int pid) => null;
        public string? ReadEnviron(int pid) => null;
        public string? ReadNetTcp() => NetTcp;
        public string? ReadNetTcp6() => NetTcp6;
        public int? FindPidOwningSocketInode(long inode) => InodeOwners.TryGetValue(inode, out var pid) ? pid : null;
        public string? ReadCmdlineFirstLine(int pid) => Cmdlines.TryGetValue(pid, out var cmdline) ? cmdline : null;
    }

    [TestMethod]
    public void FindEstablishedInode_MatchesLocalPort_Ipv4()
    {
        var inode = LinuxSshSessionWindowResolver.FindEstablishedInode(TcpFixture, 40000);

        Assert.AreEqual(55001L, inode);
    }

    [TestMethod]
    public void FindEstablishedInode_MatchesLocalPort_Ipv6()
    {
        var inode = LinuxSshSessionWindowResolver.FindEstablishedInode(Tcp6Fixture, 40000);

        Assert.AreEqual(66001L, inode);
    }

    [TestMethod]
    public void FindEstablishedInode_PortNotFound_ReturnsNull()
    {
        var inode = LinuxSshSessionWindowResolver.FindEstablishedInode(TcpFixture, 12345);

        Assert.IsNull(inode);
    }

    [TestMethod]
    public void FindEstablishedInode_NonEstablishedRow_Ignored()
    {
        // Port 0x0050 = 80 exists in the fixture but its state is "0A" (LISTEN), not established.
        var inode = LinuxSshSessionWindowResolver.FindEstablishedInode(TcpFixture, 80);

        Assert.IsNull(inode);
    }

    [TestMethod]
    public void FindEstablishedInode_NullOrEmptyContents_ReturnsNull()
    {
        Assert.IsNull(LinuxSshSessionWindowResolver.FindEstablishedInode(null, 40000));
        Assert.IsNull(LinuxSshSessionWindowResolver.FindEstablishedInode("", 40000));
    }

    [TestMethod]
    public void ResolveWindow_NonPositivePort_ReturnsNullWithoutReadingProc()
    {
        var fake = new FakeProc();

        Assert.IsNull(new LinuxSshSessionWindowResolver(fake).ResolveWindow(0));
        Assert.IsNull(new LinuxSshSessionWindowResolver(fake).ResolveWindow(-1));
    }

    [TestMethod]
    public void ResolveWindow_HappyPath_ReturnsPidTokenAndCmdlineTitle()
    {
        var fake = new FakeProc
        {
            NetTcp = TcpFixture,
            InodeOwners = { [55001] = 4242 },
            Comms = { [4242] = "ssh" },
            Cmdlines = { [4242] = "ssh user@remote-host" }
        };

        var resolved = new LinuxSshSessionWindowResolver(fake).ResolveWindow(40000);

        Assert.IsNotNull(resolved);
        Assert.AreEqual(WindowToken.FromHandle(4242), resolved.Value.Window);
        Assert.AreEqual("ssh user@remote-host", resolved.Value.Title);
    }

    [TestMethod]
    public void ResolveWindow_OwnerIsNotSsh_Rejected()
    {
        var fake = new FakeProc
        {
            NetTcp = TcpFixture,
            InodeOwners = { [55001] = 4242 },
            Comms = { [4242] = "bash" }
        };

        Assert.IsNull(new LinuxSshSessionWindowResolver(fake).ResolveWindow(40000));
    }

    [TestMethod]
    public void ResolveWindow_NoOwningPidFound_ReturnsNull()
    {
        var fake = new FakeProc { NetTcp = TcpFixture };

        Assert.IsNull(new LinuxSshSessionWindowResolver(fake).ResolveWindow(40000));
    }

    [TestMethod]
    public void ResolveWindow_PortNotInEitherTable_ReturnsNull()
    {
        var fake = new FakeProc { NetTcp = TcpFixture, NetTcp6 = Tcp6Fixture };

        Assert.IsNull(new LinuxSshSessionWindowResolver(fake).ResolveWindow(9999));
    }

    [TestMethod]
    public void ResolveWindow_FallsBackToTcp6_WhenNotInTcp4()
    {
        var fake = new FakeProc
        {
            NetTcp = "",
            NetTcp6 = Tcp6Fixture,
            InodeOwners = { [66001] = 777 },
            Comms = { [777] = "ssh" }
        };

        var resolved = new LinuxSshSessionWindowResolver(fake).ResolveWindow(40000);

        Assert.IsNotNull(resolved);
        Assert.AreEqual(WindowToken.FromHandle(777), resolved.Value.Window);
        Assert.AreEqual("", resolved.Value.Title);
    }
}
