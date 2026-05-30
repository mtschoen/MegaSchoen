using DisplayManager.Core.Models;

namespace DisplayManager.Core.Services;

/// <summary>
/// Pure normalization of a draft layout into a valid Windows multi-monitor arrangement:
/// exactly one primary anchored at (0,0), no overlaps, and no gaps — every monitor is
/// part of one contiguous region (each touches another along an edge). Footprint respects
/// rotation (90/270 swap width/height).
///
/// Uses a greedy "shrinkwrap": the primary stays put, then each remaining monitor (nearest
/// first) is snapped flush against the already-placed cluster, choosing the minimal-movement
/// edge that introduces no overlap. Snapping flush (rather than only resolving overlaps)
/// closes gaps and keeps the layout connected, while min-movement preserves the user's rough
/// arrangement. Used by the explicit Normalize button and as a guard before any apply.
/// </summary>
public static class LayoutNormalizer
{
    public static List<SavedDisplayConfig> Normalize(IReadOnlyList<SavedDisplayConfig> displays)
    {
        var result = displays.Select(Clone).ToList();
        if (result.Count == 0)
        {
            return result;
        }

        // Exactly one primary: keep the first flagged, else promote the first monitor.
        var primary = result.FirstOrDefault(d => d.IsPrimary) ?? result[0];
        foreach (var d in result)
        {
            d.IsPrimary = ReferenceEquals(d, primary);
        }

        // Shrinkwrap the rest onto the primary, nearest first, so each snaps to an
        // already-placed neighbor and the whole set stays connected.
        var placed = new List<SavedDisplayConfig> { primary };
        var remaining = result
            .Where(d => !ReferenceEquals(d, primary))
            .OrderBy(d => Distance(d, primary))
            .ToList();
        foreach (var monitor in remaining)
        {
            SnapToCluster(monitor, placed);
            placed.Add(monitor);
        }

        // Anchor the primary's top-left at (0,0) by translating the whole layout.
        var offsetX = primary.PositionX;
        var offsetY = primary.PositionY;
        foreach (var d in result)
        {
            d.PositionX -= offsetX;
            d.PositionY -= offsetY;
        }

        return result;
    }

    /// <summary>
    /// Move <paramref name="monitor"/> flush against the placed cluster. Considers each
    /// placed rect's four outer edges, aligning the perpendicular axis to the monitor's
    /// current coordinate (clamped so the shared edge has positive length), and picks the
    /// non-overlapping candidate requiring the least movement.
    /// </summary>
    static void SnapToCluster(SavedDisplayConfig monitor, List<SavedDisplayConfig> placed)
    {
        var (mw, mh) = Footprint(monitor);
        var found = false;
        var bestCost = long.MaxValue;
        var bestX = monitor.PositionX;
        var bestY = monitor.PositionY;

        foreach (var p in placed)
        {
            var (pw, ph) = Footprint(p);
            var alignedY = ClampOverlap(monitor.PositionY, p.PositionY, ph, mh);
            var alignedX = ClampOverlap(monitor.PositionX, p.PositionX, pw, mw);
            var candidates = new[]
            {
                (x: p.PositionX + pw, y: alignedY), // right of p
                (x: p.PositionX - mw, y: alignedY), // left of p
                (x: alignedX, y: p.PositionY + ph), // below p
                (x: alignedX, y: p.PositionY - mh)  // above p
            };

            foreach (var (x, y) in candidates)
            {
                if (OverlapsAny(x, y, mw, mh, placed))
                {
                    continue;
                }
                var cost = Math.Abs((long)x - monitor.PositionX) + Math.Abs((long)y - monitor.PositionY);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestX = x;
                    bestY = y;
                    found = true;
                }
            }
        }

        if (found)
        {
            monitor.PositionX = bestX;
            monitor.PositionY = bestY;
        }
    }

    // Clamp the moving rect's perpendicular coordinate so it shares a positive-length edge
    // with the anchor rect, staying as close as possible to its current coordinate.
    static int ClampOverlap(int movingCoordinate, int anchorCoordinate, int anchorSize, int movingSize)
    {
        var low = anchorCoordinate - movingSize + 1;
        var high = anchorCoordinate + anchorSize - 1;
        return Math.Clamp(movingCoordinate, low, high);
    }

    static bool OverlapsAny(int x, int y, int width, int height, List<SavedDisplayConfig> placed)
    {
        foreach (var p in placed)
        {
            var (pw, ph) = Footprint(p);
            if (x < p.PositionX + pw && x + width > p.PositionX
                && y < p.PositionY + ph && y + height > p.PositionY)
            {
                return true;
            }
        }
        return false;
    }

    static long Distance(SavedDisplayConfig a, SavedDisplayConfig b) =>
        Math.Abs((long)a.PositionX - b.PositionX) + Math.Abs((long)a.PositionY - b.PositionY);

    static SavedDisplayConfig Clone(SavedDisplayConfig d) => new()
    {
        MonitorName = d.MonitorName,
        EdidManufactureId = d.EdidManufactureId,
        EdidProductCodeId = d.EdidProductCodeId,
        EdidSerialNumber = d.EdidSerialNumber,
        EdidManufactureDate = d.EdidManufactureDate,
        EdidContainerId = d.EdidContainerId,
        Width = d.Width,
        Height = d.Height,
        PositionX = d.PositionX,
        PositionY = d.PositionY,
        RefreshRate = d.RefreshRate,
        Rotation = d.Rotation,
        IsPrimary = d.IsPrimary
    };

    static (int Width, int Height) Footprint(SavedDisplayConfig d) =>
        d.Rotation is 90 or 270 ? (d.Height, d.Width) : (d.Width, d.Height);
}
