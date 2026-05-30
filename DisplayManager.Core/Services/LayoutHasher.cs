using System.Security.Cryptography;
using System.Text;
using DisplayManager.Core.Models;

namespace DisplayManager.Core.Services;

/// <summary>
/// Produces a stable, order-independent hash of a layout over exactly the fields
/// that <see cref="DriftReport"/> compares (position, resolution, rotation, refresh,
/// primary), keyed by EDID identity. The verified-before-commit stamp compares this
/// hash: editing after a successful test changes the hash and invalidates the stamp.
/// </summary>
public static class LayoutHasher
{
    public static string Hash(IReadOnlyList<SavedDisplayConfig> displays)
    {
        var lines = displays
            .Select(d => string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{d.EdidManufactureId}|{d.EdidProductCodeId}|{d.EdidSerialNumber}|{d.PositionX}|{d.PositionY}|{d.Width}|{d.Height}|{d.RefreshRate:F3}|{d.Rotation}|{d.IsPrimary}"))
            .OrderBy(s => s, StringComparer.Ordinal);

        var canonical = string.Join("\n", lines);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }
}
