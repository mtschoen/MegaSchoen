using DisplayManager.Core;
using DisplayManager.Core.Models;
using DisplayManager.Core.Services;

namespace DisplayManager.Core.Tests;

[TestClass]
public class DisplayDriftServiceTests
{
    static DisplayInfo Live(int mfg, int prod, string serial, int x, int y,
        int w = 3840, int h = 2160, double hz = 60.0, int rot = 0, bool primary = false)
        => new()
        {
            MonitorName = $"{mfg}-{prod}", IsActive = true, IsPrimary = primary,
            EdidManufactureId = mfg, EdidProductCodeId = prod, EdidSerialNumber = serial,
            PositionX = x, PositionY = y, Width = w, Height = h, RefreshRate = hz, Rotation = rot
        };

    static SavedDisplayConfig Cfg(int mfg, int prod, string serial, int x, int y,
        int w = 3840, int h = 2160, double hz = 60.0, int rot = 0, bool primary = false)
        => new()
        {
            MonitorName = $"{mfg}-{prod}", EdidManufactureId = mfg, EdidProductCodeId = prod,
            EdidSerialNumber = serial, PositionX = x, PositionY = y, Width = w, Height = h,
            RefreshRate = hz, Rotation = rot, IsPrimary = primary
        };

    static SavedDisplayProfile Profile(params SavedDisplayConfig[] displays)
        => new() { Name = "T", Displays = displays.ToList() };

    [TestMethod]
    public void Compare_ExactMatch_ReportsMatch()
    {
        var live = new List<DisplayInfo> { Live(1, 2, "A", 0, 0, primary: true) };
        var report = new DisplayDriftService().Compare(live, Profile(Cfg(1, 2, "A", 0, 0, primary: true)));

        Assert.IsTrue(report.Matches);
        Assert.AreEqual(1, report.Monitors.Count);
        Assert.AreEqual(DriftKind.Match, report.Monitors[0].Kind);
    }

    [TestMethod]
    public void Compare_PositionDiffers_ReportsFieldMismatch()
    {
        var live = new List<DisplayInfo> { Live(1, 2, "A", 100, 0, primary: true) };
        var report = new DisplayDriftService().Compare(live, Profile(Cfg(1, 2, "A", 0, 0, primary: true)));

        Assert.IsFalse(report.Matches);
        Assert.AreEqual(DriftKind.FieldMismatch, report.Monitors[0].Kind);
        Assert.IsTrue(report.Monitors[0].Mismatches.Exists(m => m.StartsWith("position")));
    }

    [TestMethod]
    public void Compare_RefreshWithinTolerance_IsMatch()
    {
        // 59.997 capture vs 60.0 profile — must NOT count as drift.
        var live = new List<DisplayInfo> { Live(1, 2, "A", 0, 0, hz: 59.997, primary: true) };
        var report = new DisplayDriftService().Compare(live, Profile(Cfg(1, 2, "A", 0, 0, hz: 60.0, primary: true)));

        Assert.IsTrue(report.Matches);
    }

    [TestMethod]
    public void Compare_RefreshBeyondTolerance_IsFieldMismatch()
    {
        var live = new List<DisplayInfo> { Live(1, 2, "A", 0, 0, hz: 144.0, primary: true) };
        var report = new DisplayDriftService().Compare(live, Profile(Cfg(1, 2, "A", 0, 0, hz: 60.0, primary: true)));

        Assert.AreEqual(DriftKind.FieldMismatch, report.Monitors[0].Kind);
        Assert.IsTrue(report.Monitors[0].Mismatches.Exists(m => m.StartsWith("refresh")));
    }

    [TestMethod]
    public void Compare_RotationDiffers_ReportsFieldMismatch()
    {
        var live = new List<DisplayInfo> { Live(1, 2, "A", 0, 0, rot: 0, primary: true) };
        var report = new DisplayDriftService().Compare(live, Profile(Cfg(1, 2, "A", 0, 0, rot: 270, primary: true)));

        Assert.IsTrue(report.Monitors[0].Mismatches.Exists(m => m.StartsWith("rotation")));
    }

    [TestMethod]
    public void Compare_PrimaryDiffers_ReportsFieldMismatch()
    {
        var live = new List<DisplayInfo> { Live(1, 2, "A", 0, 0, primary: false) };
        var report = new DisplayDriftService().Compare(live, Profile(Cfg(1, 2, "A", 0, 0, primary: true)));

        Assert.IsTrue(report.Monitors[0].Mismatches.Exists(m => m.StartsWith("primary")));
    }

    [TestMethod]
    public void Compare_ProfileMonitorMissing_ReportsNotConnected()
    {
        var live = new List<DisplayInfo>(); // nothing active
        var report = new DisplayDriftService().Compare(live, Profile(Cfg(1, 2, "A", 0, 0, primary: true)));

        Assert.IsFalse(report.Matches);
        Assert.AreEqual(DriftKind.MonitorNotConnected, report.Monitors[0].Kind);
    }

    [TestMethod]
    public void Compare_ExtraActiveMonitor_ReportsUnexpected()
    {
        var live = new List<DisplayInfo>
        {
            Live(1, 2, "A", 0, 0, primary: true),
            Live(9, 9, "Z", 3840, 0)
        };
        var report = new DisplayDriftService().Compare(live, Profile(Cfg(1, 2, "A", 0, 0, primary: true)));

        Assert.IsFalse(report.Matches);
        Assert.IsTrue(report.Monitors.Exists(m => m.Kind == DriftKind.UnexpectedActiveMonitor));
    }

    [TestMethod]
    public void Compare_IdenticalModelsDifferentSerial_MatchesBySerial()
    {
        // Two monitors, same mfg+product, different serial and position.
        var live = new List<DisplayInfo>
        {
            Live(1, 2, "LEFT", 0, 0, primary: true),
            Live(1, 2, "RIGHT", 3840, 0)
        };
        var report = new DisplayDriftService().Compare(live, Profile(
            Cfg(1, 2, "LEFT", 0, 0, primary: true),
            Cfg(1, 2, "RIGHT", 3840, 0)));

        Assert.IsTrue(report.Matches, "serial should disambiguate identical models to the correct positions");
    }

    [TestMethod]
    public void Compare_InactiveLiveDisplays_AreIgnored()
    {
        var live = new List<DisplayInfo>
        {
            Live(1, 2, "A", 0, 0, primary: true),
            new() { EdidManufactureId = 5, EdidProductCodeId = 5, IsActive = false }
        };
        var report = new DisplayDriftService().Compare(live, Profile(Cfg(1, 2, "A", 0, 0, primary: true)));

        Assert.IsTrue(report.Matches);
    }

    [TestMethod]
    public void Compare_ResolutionDiffers_ReportsFieldMismatch()
    {
        var live = new List<DisplayInfo> { Live(1, 2, "A", 0, 0, w: 2560, h: 1440, primary: true) };
        var report = new DisplayDriftService().Compare(live, Profile(Cfg(1, 2, "A", 0, 0, w: 3840, h: 2160, primary: true)));

        Assert.AreEqual(DriftKind.FieldMismatch, report.Monitors[0].Kind);
        Assert.IsTrue(report.Monitors[0].Mismatches.Exists(m => m.StartsWith("resolution")));
    }

    [TestMethod]
    public void Compare_EmptyProfile_DoesNotMatch()
    {
        // An empty profile has nothing to confirm — Matches is false by design (Count > 0 guard),
        // so it can never serve as a satisfied commit gate.
        var report = new DisplayDriftService().Compare(new List<DisplayInfo>(), Profile());
        Assert.IsFalse(report.Matches);
        Assert.AreEqual(0, report.Monitors.Count);
    }
}
