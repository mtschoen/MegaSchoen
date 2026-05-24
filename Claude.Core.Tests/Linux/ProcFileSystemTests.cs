using Claude.Core.Linux;

namespace Claude.Core.Tests.Linux;

[TestClass]
public class ProcFileSystemTests
{
    [TestMethod]
    public void BootTimeEpochSeconds_ParsesBtimeLine()
    {
        var sut = new ProcFileSystem(statContents: "cpu  1 2 3\nbtime 1779252757\nprocesses 99\n");
        Assert.AreEqual(1779252757L, sut.BootTimeEpochSeconds);
    }
}
