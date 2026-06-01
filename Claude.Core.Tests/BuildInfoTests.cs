using Claude.Core;

namespace Claude.Core.Tests;

[TestClass]
public class BuildInfoTests
{
    [TestMethod]
    public void Version_IsNonEmpty()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(BuildInfo.Version));
    }

    [TestMethod]
    public void DescribeFor_NullAttribute_FallsBackToUnknown()
    {
        Assert.AreEqual("unknown", BuildInfo.Normalize(null));
    }

    [TestMethod]
    public void Normalize_StripsAssemblyMetadataPlusGarbage_KeepsVersionAndHash()
    {
        Assert.AreEqual("0.1.0+abc1234", BuildInfo.Normalize("0.1.0+abc1234"));
        Assert.AreEqual("0.1.0+abc1234-dirty", BuildInfo.Normalize("0.1.0+abc1234-dirty"));
    }

    [TestMethod]
    public void BuildStamp_IgnoresSemVerPrefix_MatchesAcrossDifferingPrefixes()
    {
        // The MAUI app (1.0+hash) and a library (0.1.0+hash) built from the same
        // commit must share a stamp so the stale-bridge guardrail does not false-fire.
        Assert.AreEqual(
            BuildInfo.BuildStamp("0.1.0+596a49b-dirty"),
            BuildInfo.BuildStamp("1.0+596a49b-dirty"));
        Assert.AreEqual("596a49b-dirty", BuildInfo.BuildStamp("1.0+596a49b-dirty"));
        Assert.AreEqual("missing", BuildInfo.BuildStamp("missing"));
        Assert.AreNotEqual(BuildInfo.BuildStamp("1.0+aaaaaaa"), BuildInfo.BuildStamp("0.1.0+bbbbbbb"));
    }

    [TestMethod]
    public void VersionOfFile_MissingPath_ReturnsMissing()
    {
        Assert.AreEqual("missing", BuildInfo.VersionOfFile(
            Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.dll")));
    }

    [TestMethod]
    public void VersionOfFile_OwnAssembly_MatchesRuntimeVersion()
    {
        var path = typeof(BuildInfo).Assembly.Location;
        Assert.AreEqual(BuildInfo.VersionFor(typeof(BuildInfo).Assembly), BuildInfo.VersionOfFile(path));
    }
}
