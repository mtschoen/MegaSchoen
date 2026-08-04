namespace DisplayManager.Core.Tests;

[TestClass]
public class DisplayProfileDataPathsTests
{
    [TestMethod]
    public void ResolveDirectory_OverrideIsSet_ReturnsAbsoluteOverride()
    {
        var relativeOverride = Path.Combine("fixtures", "portable-data");

        var result = DisplayProfileDataPaths.ResolveDirectory(relativeOverride, "ignored");

        Assert.AreEqual(Path.GetFullPath(relativeOverride), result);
    }

    [TestMethod]
    public void ResolveDirectory_OverrideIsBlank_ReturnsMegaSchoenUnderPlatformDirectory()
    {
        var platformDirectory = Path.Combine(Path.GetTempPath(), "platform-data");

        var result = DisplayProfileDataPaths.ResolveDirectory("  ", platformDirectory);

        Assert.AreEqual(Path.Combine(platformDirectory, "MegaSchoen"), result);
    }
}
