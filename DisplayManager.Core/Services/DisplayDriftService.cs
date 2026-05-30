using DisplayManager.Core.Models;

namespace DisplayManager.Core.Services;

/// <summary>
/// Compares the live display configuration to a saved profile and reports drift.
/// Pure comparison logic (the <see cref="Compare"/> overload) is unit-testable with
/// synthetic data; <see cref="CompareToLive"/> wires in the live enumeration.
/// </summary>
public class DisplayDriftService
{
    const double RefreshToleranceHz = 0.1;

    /// <summary>Compares a live display snapshot to a profile. Pure — no native calls.</summary>
    public DriftReport Compare(IReadOnlyList<DisplayInfo> liveDisplays, SavedDisplayProfile profile)
    {
        var report = new DriftReport();
        var activeLive = liveDisplays.Where(d => d.IsActive).ToList();
        var consumed = new HashSet<DisplayInfo>();

        foreach (var want in profile.Displays)
        {
            var match = activeLive.FirstOrDefault(d => !consumed.Contains(d) && EdidMatches(d, want));
            if (match is null)
            {
                report.Monitors.Add(new MonitorDrift
                {
                    MonitorName = want.MonitorName,
                    EdidManufactureId = want.EdidManufactureId,
                    EdidProductCodeId = want.EdidProductCodeId,
                    EdidSerialNumber = want.EdidSerialNumber,
                    Kind = DriftKind.MonitorNotConnected
                });
                continue;
            }

            consumed.Add(match);
            var mismatches = FieldMismatches(match, want);
            report.Monitors.Add(new MonitorDrift
            {
                MonitorName = want.MonitorName,
                EdidManufactureId = want.EdidManufactureId,
                EdidProductCodeId = want.EdidProductCodeId,
                EdidSerialNumber = want.EdidSerialNumber,
                Kind = mismatches.Count == 0 ? DriftKind.Match : DriftKind.FieldMismatch,
                Mismatches = mismatches
            });
        }

        foreach (var extra in activeLive.Where(d => !consumed.Contains(d)))
        {
            report.Monitors.Add(new MonitorDrift
            {
                MonitorName = extra.MonitorName,
                EdidManufactureId = extra.EdidManufactureId,
                EdidProductCodeId = extra.EdidProductCodeId,
                EdidSerialNumber = extra.EdidSerialNumber,
                Kind = DriftKind.UnexpectedActiveMonitor
            });
        }

        return report;
    }

    /// <summary>Compares the current live configuration to a profile.</summary>
    public DriftReport CompareToLive(SavedDisplayProfile profile)
        => Compare(DisplayManager.GetAllDisplays(), profile);

    // EDID cascade mirrors the native matcher: manufacturer + product always;
    // serial only when BOTH sides have a non-empty serial (disambiguates identical models).
    // Comparing serial only when both sides are non-empty is deliberately identical to the
    // native EDID matcher in DisplayManagerNative.cpp, so drift detection stays consistent with
    // what ApplyConfiguration actually matched.
    static bool EdidMatches(DisplayInfo live, SavedDisplayConfig want)
    {
        if (live.EdidManufactureId != want.EdidManufactureId) return false;
        if (live.EdidProductCodeId != want.EdidProductCodeId) return false;
        if (!string.IsNullOrEmpty(want.EdidSerialNumber) && !string.IsNullOrEmpty(live.EdidSerialNumber)
            && !string.Equals(want.EdidSerialNumber, live.EdidSerialNumber, StringComparison.Ordinal))
        {
            return false;
        }
        return true;
    }

    static List<string> FieldMismatches(DisplayInfo live, SavedDisplayConfig want)
    {
        var diffs = new List<string>();
        if (live.PositionX != want.PositionX || live.PositionY != want.PositionY)
        {
            diffs.Add($"position: live ({live.PositionX},{live.PositionY}) vs profile ({want.PositionX},{want.PositionY})");
        }
        if (live.Width != want.Width || live.Height != want.Height)
        {
            diffs.Add($"resolution: live {live.Width}x{live.Height} vs profile {want.Width}x{want.Height}");
        }
        if (live.Rotation != want.Rotation)
        {
            diffs.Add($"rotation: live {live.Rotation} vs profile {want.Rotation}");
        }
        if (Math.Abs(live.RefreshRate - want.RefreshRate) > RefreshToleranceHz)
        {
            diffs.Add($"refresh: live {live.RefreshRate:F3} vs profile {want.RefreshRate:F3}");
        }
        if (live.IsPrimary != want.IsPrimary)
        {
            diffs.Add($"primary: live {live.IsPrimary} vs profile {want.IsPrimary}");
        }
        return diffs;
    }
}
