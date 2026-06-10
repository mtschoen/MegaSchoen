using Claude.Core;

namespace Claude.Core.Tests;

[TestClass]
public class SshSessionWindowResolverTests
{
    static SshSessionWindowResolver Build(
        Func<int, uint?> portToPid,
        Func<uint, string?> processName,
        Func<uint, AncestorWindowResolver.WindowHit?> ancestor) =>
        new(portToPid, processName, ancestor);

    [TestMethod]
    public void Resolve_PortToSsh_ToWindow_ReturnsHwnd()
    {
        var sut = Build(
            portToPid: port => port == 51000 ? 800u : null,
            processName: pid => pid == 800 ? "ssh" : null,
            ancestor: pid => pid == 800 ? new AncestorWindowResolver.WindowHit(new IntPtr(0xABC), "cmd") : null);

        var hit = sut.Resolve(51000);

        Assert.IsNotNull(hit);
        Assert.AreEqual(new IntPtr(0xABC), hit.Value.Hwnd);
        Assert.AreEqual("cmd", hit.Value.Title);
    }

    [TestMethod]
    public void Resolve_NoMatchingConnection_ReturnsNull()
    {
        var sut = Build(_ => null, _ => "ssh", _ => new AncestorWindowResolver.WindowHit(new IntPtr(1), "x"));
        Assert.IsNull(sut.Resolve(51000));
    }

    [TestMethod]
    public void Resolve_OwningProcessNotSsh_ReturnsNull()
    {
        // Port matched a non-ssh process (e.g. our own MegaSchoen streaming probe
        // would be ssh, but any stale match that is not ssh.exe is rejected).
        var sut = Build(_ => 900u, pid => "chrome", _ => new AncestorWindowResolver.WindowHit(new IntPtr(1), "x"));
        Assert.IsNull(sut.Resolve(51000));
    }

    [TestMethod]
    public void Resolve_SshButNoAncestorWindow_ReturnsNull()
    {
        var sut = Build(_ => 800u, _ => "ssh", _ => null);
        Assert.IsNull(sut.Resolve(51000));
    }

    [TestMethod]
    public void Resolve_NullOrZeroPort_ReturnsNull()
    {
        var sut = Build(_ => 800u, _ => "ssh", _ => new AncestorWindowResolver.WindowHit(new IntPtr(1), "x"));
        Assert.IsNull(sut.Resolve(0));
    }
}
