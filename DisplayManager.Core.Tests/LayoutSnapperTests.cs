using DisplayManager.Core.Services;
using Rect = DisplayManager.Core.Services.LayoutSnapper.SnapRect;

namespace DisplayManager.Core.Tests;

[TestClass]
public class LayoutSnapperTests
{
    // A 4K neighbor anchored at the origin; threshold large enough to model a real drag near it.
    static readonly Rect Neighbor = new(0, 0, 3840, 2160);
    const int Threshold = 400;

    [TestMethod]
    public void SnapsLeftEdgeFlushToNeighborRight()
    {
        // Dragged just short of the neighbor's right edge → snaps flush to the right of it.
        var (x, y) = LayoutSnapper.Snap(new Rect(3800, 30, 1920, 1080), [Neighbor], Threshold);
        Assert.AreEqual(3840, x); // moving.left → neighbor.right
        Assert.AreEqual(0, y);    // tops align
    }

    [TestMethod]
    public void SnapsRightEdgeFlushToNeighborLeft()
    {
        // Dragged so its right edge is near the neighbor's left edge → snaps to the left of it.
        var (x, _) = LayoutSnapper.Snap(new Rect(-1900, 0, 1920, 1080), [Neighbor], Threshold);
        Assert.AreEqual(-1920, x); // moving.right (x+1920) → neighbor.left (0)
    }

    [TestMethod]
    public void SnapsBelowNeighbor()
    {
        var (x, y) = LayoutSnapper.Snap(new Rect(20, 2150, 1920, 1080), [Neighbor], Threshold);
        Assert.AreEqual(0, x);    // left edges align
        Assert.AreEqual(2160, y); // moving.top → neighbor.bottom
    }

    [TestMethod]
    public void AlignsTopEdgesWithoutAdjacency()
    {
        // Far to the right horizontally (no X candidate in range) but tops nearly aligned.
        var (x, y) = LayoutSnapper.Snap(new Rect(9000, 25, 1920, 1080), [Neighbor], Threshold);
        Assert.AreEqual(9000, x); // no X edge within threshold → unchanged
        Assert.AreEqual(0, y);    // tops align
    }

    [TestMethod]
    public void DoesNotSnapBeyondThreshold()
    {
        var moving = new Rect(9000, 9000, 1920, 1080);
        var (x, y) = LayoutSnapper.Snap(moving, [Neighbor], Threshold);
        Assert.AreEqual(9000, x);
        Assert.AreEqual(9000, y);
    }

    [TestMethod]
    public void PicksClosestEdgeAmongNeighbors()
    {
        var a = new Rect(0, 0, 1920, 1080);
        var b = new Rect(4000, 0, 1920, 1080);
        // moving.left=3950 is 30px from b.left(4000) and 2030px from a.right(1920) → aligns to b's left edge.
        var (x, _) = LayoutSnapper.Snap(new Rect(3950, 0, 1920, 1080), [a, b], Threshold);
        Assert.AreEqual(4000, x);
    }

    [TestMethod]
    public void EmptyNeighbors_ReturnsUnchanged()
    {
        var (x, y) = LayoutSnapper.Snap(new Rect(123, 456, 1920, 1080), [], Threshold);
        Assert.AreEqual(123, x);
        Assert.AreEqual(456, y);
    }
}
