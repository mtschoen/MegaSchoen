using Claude.Core;

namespace Claude.Core.Tests;

[TestClass]
public class SshConnectionParserTests
{
    // environ is a NUL-delimited list of KEY=VALUE pairs (as in /proc/<pid>/environ).
    static string Environ(params string[] pairs) => string.Join('\0', pairs) + '\0';

    [TestMethod]
    public void TryParseClientPort_ExtractsSecondField()
    {
        // SSH_CONNECTION = "<clientIp> <clientPort> <serverIp> <serverPort>"
        var env = Environ("PATH=/usr/bin", "SSH_CONNECTION=192.168.1.50 54321 192.168.1.9 22", "HOME=/home/schoen");
        Assert.IsTrue(SshConnectionParser.TryParseClientPort(env, out var port));
        Assert.AreEqual(54321, port);
    }

    [TestMethod]
    public void TryParseClientPort_IpV6Server_StillParsesClientPort()
    {
        var env = Environ("SSH_CONNECTION=fe80::1 60000 fe80::2 22");
        Assert.IsTrue(SshConnectionParser.TryParseClientPort(env, out var port));
        Assert.AreEqual(60000, port);
    }

    [TestMethod]
    public void TryParseClientPort_NoSshConnection_ReturnsFalse()
    {
        var env = Environ("PATH=/usr/bin", "HOME=/home/schoen");
        Assert.IsFalse(SshConnectionParser.TryParseClientPort(env, out var port));
        Assert.AreEqual(0, port);
    }

    [TestMethod]
    public void TryParseClientPort_Malformed_ReturnsFalse()
    {
        Assert.IsFalse(SshConnectionParser.TryParseClientPort(Environ("SSH_CONNECTION=garbage"), out _));
        Assert.IsFalse(SshConnectionParser.TryParseClientPort(Environ("SSH_CONNECTION=1.2.3.4 notaport 5.6.7.8 22"), out _));
        Assert.IsFalse(SshConnectionParser.TryParseClientPort(null, out _));
        Assert.IsFalse(SshConnectionParser.TryParseClientPort("", out _));
    }
}
