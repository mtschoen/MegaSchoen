namespace ClaudeCycler.Core.Tests;

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
    public void Install_MissingFile_CreatesWithThreeHooks()
    {
        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\bridge.exe");

        Assert.IsTrue(File.Exists(_tempSettings));
        var contents = File.ReadAllText(_tempSettings);
        StringAssert.Contains(contents, "Notification");
        StringAssert.Contains(contents, "UserPromptSubmit");
        StringAssert.Contains(contents, "Stop");
        StringAssert.Contains(contents, "C:\\\\bridge.exe");
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
        StringAssert.Contains(contents, "permissions");
        StringAssert.Contains(contents, "Bash(*:*)");
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
