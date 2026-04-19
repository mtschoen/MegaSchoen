namespace ClaudeCycler.Core.Tests;

[TestClass]
public class PathsTests
{
    [TestMethod]
    public void AppDataDirectory_IsUnderLocalAppData()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.AreEqual(Path.Combine(localAppData, "MegaSchoen"), Paths.AppDataDirectory);
    }

    [TestMethod]
    public void NeedySessionsFile_IsUnderAppDataDirectory()
    {
        Assert.AreEqual(Path.Combine(Paths.AppDataDirectory, "needy-sessions.json"), Paths.NeedySessionsFile);
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
}
