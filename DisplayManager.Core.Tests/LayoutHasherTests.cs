using DisplayManager.Core.Models;
using DisplayManager.Core.Services;

namespace DisplayManager.Core.Tests;

[TestClass]
public class LayoutHasherTests
{
    static SavedDisplayConfig Monitor(string serial, int x, int y, int w = 1920, int h = 1080, double hz = 60, int rot = 0, bool primary = false) =>
        new()
        {
            EdidManufactureId = 1, EdidProductCodeId = 2, EdidSerialNumber = serial,
            PositionX = x, PositionY = y, Width = w, Height = h, RefreshRate = hz,
            Rotation = rot, IsPrimary = primary
        };

    [TestMethod]
    public void SameLayout_SameHash()
    {
        var a = new List<SavedDisplayConfig> { Monitor("A", 0, 0, primary: true), Monitor("B", 1920, 0) };
        var b = new List<SavedDisplayConfig> { Monitor("A", 0, 0, primary: true), Monitor("B", 1920, 0) };
        Assert.AreEqual(LayoutHasher.Hash(a), LayoutHasher.Hash(b));
    }

    [TestMethod]
    public void OrderIndependent_SameHash()
    {
        var a = new List<SavedDisplayConfig> { Monitor("A", 0, 0, primary: true), Monitor("B", 1920, 0) };
        var b = new List<SavedDisplayConfig> { Monitor("B", 1920, 0), Monitor("A", 0, 0, primary: true) };
        Assert.AreEqual(LayoutHasher.Hash(a), LayoutHasher.Hash(b));
    }

    [TestMethod]
    public void PositionChange_DifferentHash()
    {
        var a = new List<SavedDisplayConfig> { Monitor("A", 0, 0, primary: true), Monitor("B", 1920, 0) };
        var b = new List<SavedDisplayConfig> { Monitor("A", 0, 0, primary: true), Monitor("B", 1920, 100) };
        Assert.AreNotEqual(LayoutHasher.Hash(a), LayoutHasher.Hash(b));
    }

    [TestMethod]
    public void RotationChange_DifferentHash()
    {
        var a = new List<SavedDisplayConfig> { Monitor("A", 0, 0, rot: 0, primary: true) };
        var b = new List<SavedDisplayConfig> { Monitor("A", 0, 0, rot: 90, primary: true) };
        Assert.AreNotEqual(LayoutHasher.Hash(a), LayoutHasher.Hash(b));
    }
}
