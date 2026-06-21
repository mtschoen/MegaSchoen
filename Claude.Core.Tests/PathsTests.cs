namespace Claude.Core.Tests;

[TestClass]
public class PathsTests
{
    [TestMethod]
    public void AppDataDirectory_IsUnderLocalAppData()
    {
        // Mirror Paths' own resolution (env var first, OS known-folder fallback)
        // so the assertion holds under the test sandbox's LOCALAPPDATA override.
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.AreEqual(Path.Combine(localAppData, "MegaSchoen"), Paths.AppDataDirectory);
    }

    [TestMethod]
    public void NeedySessionsDirectory_IsUnderAppDataDirectory()
    {
        Assert.AreEqual(Path.Combine(Paths.AppDataDirectory, "needy-sessions"), Paths.NeedySessionsDirectory);
    }

    [TestMethod]
    public void LegacyNeedySessionsFile_IsUnderAppDataDirectory()
    {
        Assert.AreEqual(Path.Combine(Paths.AppDataDirectory, "needy-sessions.json"), Paths.LegacyNeedySessionsFile);
    }

    [TestMethod]
    public void HookBridgeLog_IsUnderAppDataDirectory()
    {
        Assert.AreEqual(Path.Combine(Paths.AppDataDirectory, "hook-bridge.log"), Paths.HookBridgeLog);
    }

    [TestMethod]
    public void ClaudeSettingsFile_IsUnderUserProfile()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.AreEqual(Path.Combine(userProfile, ".claude", "settings.json"), Paths.ClaudeSettingsFile);
    }

    [TestMethod]
    public void GetSessionFilePath_DefaultsToNeedySessionsDirectory()
    {
        Assert.AreEqual(
            Path.Combine(Paths.NeedySessionsDirectory, "abc.json"),
            Paths.GetSessionFilePath("abc"));
    }

    [TestMethod]
    public void GetSessionFilePath_RespectsDirectoryOverride()
    {
        Assert.AreEqual(
            Path.Combine("C:\\custom", "abc.json"),
            Paths.GetSessionFilePath("abc", "C:\\custom"));
    }
}
