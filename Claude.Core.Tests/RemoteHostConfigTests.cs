using Claude.Core;
using Claude.Core.Remote;

namespace Claude.Core.Tests;

[TestClass]
public class RemoteHostConfigTests
{
    string _tempFile = "";

    [TestInitialize]
    public void Setup() => _tempFile = Path.Combine(Path.GetTempPath(), $"remhosts-{Guid.NewGuid():N}.json");

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [TestMethod]
    public void DefaultPath_IsUnderAppDataDirectory()
    {
        Assert.AreEqual(Path.Combine(Paths.AppDataDirectory, "remote-hosts.json"), RemoteHostConfig.DefaultPath);
    }

    [TestMethod]
    public void Load_NoArgument_DoesNotThrowAndReturnsList()
    {
        // Reads DefaultPath, which may or may not exist on the host; either way it
        // must return a non-null list rather than throwing.
        Assert.IsNotNull(RemoteHostConfig.Load());
    }

    [TestMethod]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.IsEmpty(RemoteHostConfig.Load(_tempFile));
    }

    [TestMethod]
    public void Load_ValidJson_DropsEntriesWithBlankSshTarget()
    {
        File.WriteAllText(_tempFile, """
        [
          { "name": "box", "sshTarget": "schoen@llamabox", "remoteCli": "claude-sessions" },
          { "name": "blank", "sshTarget": "  " },
          { "name": "missing" }
        ]
        """);

        var hosts = RemoteHostConfig.Load(_tempFile);

        Assert.HasCount(1, hosts);
        Assert.AreEqual("schoen@llamabox", hosts[0].SshTarget);
        Assert.AreEqual("claude-sessions", hosts[0].RemoteCli);
    }

    [TestMethod]
    public void Load_MalformedJson_ReturnsEmpty()
    {
        File.WriteAllText(_tempFile, "{ this is not valid json");
        Assert.IsEmpty(RemoteHostConfig.Load(_tempFile));
    }

    [TestMethod]
    public void RemoteCli_DefaultsToClaudeSessions()
    {
        Assert.AreEqual("claude-sessions", new RemoteHostConfig().RemoteCli);
    }
}
