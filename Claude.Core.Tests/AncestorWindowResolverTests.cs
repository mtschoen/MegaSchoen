using Claude.Core;

namespace Claude.Core.Tests;

[TestClass]
public class AncestorWindowResolverTests
{
    // parent chain: 100 -> 200 -> 300 (root). getParent returns null past the top.
    static Func<uint, uint?> Chain(params (uint pid, uint parent)[] edges)
    {
        var map = edges.ToDictionary(e => e.pid, e => e.parent);
        return pid => map.TryGetValue(pid, out var p) ? p : (uint?)null;
    }

    [TestMethod]
    public void Resolve_ReturnsFirstAncestorWithWindow()
    {
        // shell(100, no window) -> rider(200, window) -> services(300)
        var getParent = Chain((100, 200), (200, 300));
        var windows = new Dictionary<uint, (IntPtr, string)> { [200] = (new IntPtr(0x42), "rider") };

        var hit = AncestorWindowResolver.Resolve(
            startPid: 100, getParent, windows, stopPids: new HashSet<uint>(), maxDepth: 8);

        Assert.IsNotNull(hit);
        Assert.AreEqual(new IntPtr(0x42), hit.Value.Hwnd);
        Assert.AreEqual("rider", hit.Value.Title);
    }

    [TestMethod]
    public void Resolve_SkipsStartPidItself_WhenWindowless()
    {
        // start pid 100 has no window; only ancestor 200 does -> must climb, not return null
        var getParent = Chain((100, 200));
        var windows = new Dictionary<uint, (IntPtr, string)> { [200] = (new IntPtr(7), "ide") };

        var hit = AncestorWindowResolver.Resolve(100, getParent, windows, new HashSet<uint>(), 8);

        Assert.IsNotNull(hit);
        Assert.AreEqual(new IntPtr(7), hit.Value.Hwnd);
    }

    [TestMethod]
    public void Resolve_StopsAtStopPid_ReturnsNull()
    {
        // shell(100) -> explorer(200, STOP) -> ... ; explorer must not be treated as a host
        var getParent = Chain((100, 200), (200, 300));
        var windows = new Dictionary<uint, (IntPtr, string)>
        {
            [200] = (new IntPtr(9), "File Explorer"), // would falsely match if not stopped
            [300] = (new IntPtr(10), "desktop"),
        };

        var hit = AncestorWindowResolver.Resolve(
            100, getParent, windows, stopPids: new HashSet<uint> { 200 }, maxDepth: 8);

        Assert.IsNull(hit);
    }

    [TestMethod]
    public void Resolve_RespectsMaxDepth_ReturnsNull()
    {
        var getParent = Chain((1, 2), (2, 3), (3, 4));
        var windows = new Dictionary<uint, (IntPtr, string)> { [4] = (new IntPtr(1), "far") };

        var hit = AncestorWindowResolver.Resolve(1, getParent, windows, new HashSet<uint>(), maxDepth: 2);

        Assert.IsNull(hit, "window is deeper than maxDepth hops away");
    }

    [TestMethod]
    public void Resolve_NoWindowAnywhere_ReturnsNull()
    {
        var getParent = Chain((100, 200));
        var hit = AncestorWindowResolver.Resolve(
            100, getParent, new Dictionary<uint, (IntPtr, string)>(), new HashSet<uint>(), 8);
        Assert.IsNull(hit);
    }
}
