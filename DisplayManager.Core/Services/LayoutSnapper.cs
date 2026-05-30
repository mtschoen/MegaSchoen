namespace DisplayManager.Core.Services;

/// <summary>
/// Pure live-drag snapping assist for the layout editor. Given a monitor being dragged and
/// its neighbors (all in real desktop pixels, footprints already rotation-adjusted), returns
/// the position it should snap to. Each axis snaps independently to the closest neighbor edge
/// within <c>threshold</c> — considering both flush adjacency (place beside a neighbor) and
/// edge alignment (share a left/right or top/bottom edge). The caller excludes the dragged
/// rect from <paramref name="others"/> and supplies a threshold scaled to the current canvas
/// zoom, so the assist feels consistent regardless of fit-to-view scale.
///
/// This is distinct from <see cref="LayoutNormalizer"/>: the normalizer shrinkwraps the WHOLE
/// layout flush (gap-free, on demand), while this nudges a single dragged rect onto nearby
/// edges as the user moves it.
/// </summary>
public static class LayoutSnapper
{
    /// <summary>A monitor footprint in real desktop pixels (rotation already applied).</summary>
    public readonly record struct SnapRect(int X, int Y, int Width, int Height);

    /// <summary>
    /// Return the snapped (X, Y) for <paramref name="moving"/>. Snaps each axis to the closest
    /// neighbor edge strictly within <paramref name="threshold"/> real pixels; axes with no
    /// candidate in range keep their current coordinate.
    /// </summary>
    public static (int X, int Y) Snap(SnapRect moving, IEnumerable<SnapRect> others, int threshold)
    {
        var snappedX = moving.X;
        var snappedY = moving.Y;
        // Strictly-within-threshold "closest wins", measured from the original position so the
        // first neighbor doesn't bias later comparisons.
        var bestXDistance = threshold;
        var bestYDistance = threshold;

        foreach (var other in others)
        {
            // X axis: flush adjacency (place beside) + edge alignment (share a vertical edge).
            TryAxis(moving.X, other.X + other.Width, ref snappedX, ref bestXDistance);              // moving.left → other.right
            TryAxis(moving.X, other.X - moving.Width, ref snappedX, ref bestXDistance);             // moving.right → other.left
            TryAxis(moving.X, other.X, ref snappedX, ref bestXDistance);                            // left edges align
            TryAxis(moving.X, other.X + other.Width - moving.Width, ref snappedX, ref bestXDistance); // right edges align

            // Y axis: same four relationships.
            TryAxis(moving.Y, other.Y + other.Height, ref snappedY, ref bestYDistance);              // moving.top → other.bottom
            TryAxis(moving.Y, other.Y - moving.Height, ref snappedY, ref bestYDistance);             // moving.bottom → other.top
            TryAxis(moving.Y, other.Y, ref snappedY, ref bestYDistance);                             // top edges align
            TryAxis(moving.Y, other.Y + other.Height - moving.Height, ref snappedY, ref bestYDistance); // bottom edges align
        }

        return (snappedX, snappedY);
    }

    static void TryAxis(int current, int candidate, ref int snapped, ref int bestDistance)
    {
        var distance = Math.Abs(current - candidate);
        if (distance < bestDistance)
        {
            bestDistance = distance;
            snapped = candidate;
        }
    }
}
