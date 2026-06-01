using DisplayManager.Core.Models;
using DisplayManager.Core.Services;

namespace DisplayManager.Core.Tests;

[TestClass]
public class LayoutNormalizerTests
{
    static SavedDisplayConfig Monitor(string serial, int x, int y, int w = 1920, int h = 1080, int rot = 0, bool primary = false) =>
        new()
        {
            EdidManufactureId = 1,
            EdidProductCodeId = 2,
            EdidSerialNumber = serial,
            PositionX = x,
            PositionY = y,
            Width = w,
            Height = h,
            Rotation = rot,
            IsPrimary = primary
        };

    [TestMethod]
    public void Primary_AnchoredAtOrigin()
    {
        var input = new List<SavedDisplayConfig> { Monitor("A", 500, 300, primary: true), Monitor("B", 2420, 300) };
        var result = LayoutNormalizer.Normalize(input);
        var primary = result.Single(d => d.IsPrimary);
        Assert.AreEqual(0, primary.PositionX);
        Assert.AreEqual(0, primary.PositionY);
    }

    [TestMethod]
    public void ExactlyOnePrimary()
    {
        var input = new List<SavedDisplayConfig> { Monitor("A", 0, 0, primary: true), Monitor("B", 1920, 0, primary: true) };
        var result = LayoutNormalizer.Normalize(input);
        Assert.AreEqual(1, result.Count(d => d.IsPrimary));
    }

    [TestMethod]
    public void Overlap_Resolved()
    {
        var input = new List<SavedDisplayConfig> { Monitor("A", 0, 0, primary: true), Monitor("B", 500, 0) };
        var result = LayoutNormalizer.Normalize(input);
        Assert.IsFalse(Overlaps(result));
    }

    [TestMethod]
    public void Rotation90_SwapsFootprint()
    {
        var input = new List<SavedDisplayConfig> { Monitor("A", 0, 0, rot: 90, primary: true), Monitor("B", 500, 0) };
        var result = LayoutNormalizer.Normalize(input);
        var b = result.Single(d => d.EdidSerialNumber == "B");
        Assert.AreEqual(1080, b.PositionX);
    }

    [TestMethod]
    public void Gap_ClosedToRightNeighbor()
    {
        // B sits to the right of A with a gap; normalize snaps it flush to A's right edge.
        var input = new List<SavedDisplayConfig> { Monitor("A", 0, 0, primary: true), Monitor("B", 3000, 0) };
        var result = LayoutNormalizer.Normalize(input);
        var b = result.Single(d => d.EdidSerialNumber == "B");
        Assert.AreEqual(1920, b.PositionX);
        Assert.AreEqual(0, b.PositionY);
        Assert.IsFalse(Overlaps(result));
    }

    [TestMethod]
    public void ThreeMonitorsWithGaps_AllClosed()
    {
        var input = new List<SavedDisplayConfig>
        {
            Monitor("A", 0, 0, primary: true),
            Monitor("B", 3000, 0),
            Monitor("C", 7000, 0)
        };
        var result = LayoutNormalizer.Normalize(input);
        var b = result.Single(d => d.EdidSerialNumber == "B");
        var c = result.Single(d => d.EdidSerialNumber == "C");
        Assert.AreEqual(1920, b.PositionX);
        Assert.AreEqual(3840, c.PositionX);
        Assert.IsFalse(Overlaps(result));
        Assert.IsTrue(Contiguous(result));
    }

    [TestMethod]
    public void LeftwardLayoutWithGaps_AllClosed()
    {
        // The reported bug: monitors to the LEFT of the primary, dragged apart, must
        // re-pack flush with no gap left further out.
        var input = new List<SavedDisplayConfig>
        {
            Monitor("A", 0, 0, primary: true),
            Monitor("B", -3000, 0),
            Monitor("C", -7000, 0)
        };
        var result = LayoutNormalizer.Normalize(input);
        var b = result.Single(d => d.EdidSerialNumber == "B");
        var c = result.Single(d => d.EdidSerialNumber == "C");
        Assert.AreEqual(-1920, b.PositionX);   // flush to A's left edge
        Assert.AreEqual(-3840, c.PositionX);   // flush to B's left edge
        Assert.AreEqual(0, b.PositionY);
        Assert.AreEqual(0, c.PositionY);
        Assert.IsFalse(Overlaps(result));
        Assert.IsTrue(Contiguous(result));
    }

    [TestMethod]
    public void StaggeredLayout_NoGapsNoOverlaps()
    {
        // Mirrors the real "Desk" profile shape (monitors left of and above the primary).
        var input = new List<SavedDisplayConfig>
        {
            Monitor("A", 0, 0, primary: true),
            Monitor("B", -6000, -347),
            Monitor("C", -2160, -1412)
        };
        var result = LayoutNormalizer.Normalize(input);
        Assert.IsFalse(Overlaps(result));
        Assert.IsTrue(Contiguous(result));
    }

    // Every monitor shares a positive-length edge with the cluster, and the whole set is
    // one connected (gap-free) region — Windows' multi-monitor requirement.
    static bool Contiguous(IReadOnlyList<SavedDisplayConfig> displays)
    {
        if (displays.Count <= 1)
        {
            return true;
        }
        var visited = new HashSet<int> { 0 };
        var queue = new Queue<int>();
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            for (var j = 0; j < displays.Count; j++)
            {
                if (!visited.Contains(j) && EdgeAdjacent(displays[i], displays[j]))
                {
                    visited.Add(j);
                    queue.Enqueue(j);
                }
            }
        }
        return visited.Count == displays.Count;
    }

    static bool EdgeAdjacent(SavedDisplayConfig a, SavedDisplayConfig b)
    {
        var (aw, ah) = Footprint(a);
        var (bw, bh) = Footprint(b);
        var yOverlap = Math.Min(a.PositionY + ah, b.PositionY + bh) - Math.Max(a.PositionY, b.PositionY);
        var xOverlap = Math.Min(a.PositionX + aw, b.PositionX + bw) - Math.Max(a.PositionX, b.PositionX);
        var touchVertical = (a.PositionX + aw == b.PositionX || b.PositionX + bw == a.PositionX) && yOverlap > 0;
        var touchHorizontal = (a.PositionY + ah == b.PositionY || b.PositionY + bh == a.PositionY) && xOverlap > 0;
        return touchVertical || touchHorizontal;
    }

    static bool Overlaps(IReadOnlyList<SavedDisplayConfig> displays)
    {
        for (var i = 0; i < displays.Count; i++)
        {
            for (var j = i + 1; j < displays.Count; j++)
            {
                if (RectsOverlap(displays[i], displays[j]))
                {
                    return true;
                }
            }
        }
        return false;
    }

    static bool RectsOverlap(SavedDisplayConfig a, SavedDisplayConfig b)
    {
        var (aw, ah) = Footprint(a);
        var (bw, bh) = Footprint(b);
        return a.PositionX < b.PositionX + bw && a.PositionX + aw > b.PositionX
            && a.PositionY < b.PositionY + bh && a.PositionY + ah > b.PositionY;
    }

    static (int W, int H) Footprint(SavedDisplayConfig d) =>
        d.Rotation is 90 or 270 ? (d.Height, d.Width) : (d.Width, d.Height);
}
