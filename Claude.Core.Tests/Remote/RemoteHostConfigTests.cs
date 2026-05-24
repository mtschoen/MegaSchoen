using Claude.Core.Remote;

namespace Claude.Core.Tests.Remote;

[TestClass]
public class RemoteHostConfigTests
{
    [TestMethod]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var hosts = RemoteHostConfig.Load(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.json"));
        Assert.AreEqual(0, hosts.Count);
    }

    [TestMethod]
    public void Load_ParsesHosts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hosts-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """[{ "name": "llamabox", "sshTarget": "schoen@llamabox" }]""");
        try
        {
            var hosts = RemoteHostConfig.Load(path);
            Assert.AreEqual(1, hosts.Count);
            Assert.AreEqual("llamabox", hosts[0].Name);
            Assert.AreEqual("schoen@llamabox", hosts[0].SshTarget);
            Assert.AreEqual("claude-sessions", hosts[0].RemoteCli);   // default
        }
        finally { File.Delete(path); }
    }
}
