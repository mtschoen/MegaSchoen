namespace Claude.Core.Tests;

[TestClass]
public class SettingsJsonInstallerTests
{
    string _tempSettings = "";

    [TestInitialize]
    public void Setup()
    {
        _tempSettings = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempSettings)) File.Delete(_tempSettings);
        if (File.Exists(_tempSettings + ".bak")) File.Delete(_tempSettings + ".bak");
    }

    [TestMethod]
    public void Install_MissingFile_CreatesNotificationHook()
    {
        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\bridge.exe");

        Assert.IsTrue(File.Exists(_tempSettings));
        var contents = File.ReadAllText(_tempSettings);
        Assert.Contains("Notification", contents);
        Assert.Contains("UserPromptSubmit", contents);
        Assert.Contains("Stop", contents);
        Assert.Contains("C:/bridge.exe", contents);
    }

    [TestMethod]
    public void Install_MissingFile_CreatesAllFiveHooks()
    {
        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\bridge.exe");

        var contents = File.ReadAllText(_tempSettings);
        Assert.Contains("Notification", contents);
        Assert.Contains("UserPromptSubmit", contents);
        Assert.Contains("Stop", contents);
        Assert.Contains("PostToolUse", contents);
        Assert.Contains("SessionEnd", contents);
    }

    [TestMethod]
    public void GetStatus_AfterInstall_ReportsAllFiveInstalledHere()
    {
        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\bridge.exe");

        var status = installer.GetStatus("C:\\bridge.exe");
        Assert.AreEqual(InstallState.InstalledHere, status.Notification);
        Assert.AreEqual(InstallState.InstalledHere, status.UserPromptSubmit);
        Assert.AreEqual(InstallState.InstalledHere, status.Stop);
        Assert.AreEqual(InstallState.InstalledHere, status.PostToolUse);
        Assert.AreEqual(InstallState.InstalledHere, status.SessionEnd);
    }

    [TestMethod]
    public void Install_NormalizesBackslashesToForwardSlashes()
    {
        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\Program Files\\Foo\\bridge.exe");

        var contents = File.ReadAllText(_tempSettings);
        Assert.DoesNotContain("\\\\", contents, "settings JSON must not contain escaped backslashes in the command path");
        Assert.Contains("C:/Program Files/Foo/bridge.exe", contents);
    }

    [TestMethod]
    public void Install_PreservesUnrelatedFields()
    {
        File.WriteAllText(_tempSettings, """
        { "permissions": { "allow": ["Bash(*:*)"] } }
        """);

        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\bridge.exe");

        var contents = File.ReadAllText(_tempSettings);
        Assert.Contains("permissions", contents);
        Assert.Contains("Bash(*:*)", contents);
    }

    [TestMethod]
    public void Install_CreatesBackup()
    {
        File.WriteAllText(_tempSettings, "{}");
        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\bridge.exe");

        Assert.IsTrue(File.Exists(_tempSettings + ".bak"));
    }

    [TestMethod]
    public void Install_Idempotent_DoesNotDuplicate()
    {
        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\bridge.exe");
        installer.Install("C:\\bridge.exe");

        var status = installer.GetStatus("C:\\bridge.exe");
        Assert.AreEqual(InstallState.InstalledHere, status.Notification);
        Assert.AreEqual(InstallState.InstalledHere, status.UserPromptSubmit);
        Assert.AreEqual(InstallState.InstalledHere, status.Stop);
        Assert.AreEqual(InstallState.InstalledHere, status.PostToolUse);
        Assert.AreEqual(InstallState.InstalledHere, status.SessionEnd);
    }

    [TestMethod]
    public void GetStatus_DifferentPath_ReportsElsewhere()
    {
        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\other.exe");

        var status = installer.GetStatus("C:\\bridge.exe");
        Assert.AreEqual(InstallState.InstalledElsewhere, status.Notification);
    }

    [TestMethod]
    public void GetStatus_MissingFile_ReportsNotInstalled()
    {
        var installer = new SettingsJsonInstaller(_tempSettings);
        var status = installer.GetStatus("C:\\bridge.exe");
        Assert.AreEqual(InstallState.NotInstalled, status.Notification);
    }
}
