using Claude.Core;

namespace Claude.Core.Tests;

[TestClass]
public class EnvironmentProcessLocatorTests
{
    [TestMethod]
    public void Parse_JsonArray_YieldsWindowlessLiveSessionsPerCount()
    {
        var json = """[{"cwd":"C:/a","count":2},{"cwd":"C:/b","count":1}]""";
        var sessions = EnvironmentProcessLocator.Parse(json);

        Assert.AreEqual(3, sessions.Count);
        Assert.AreEqual(2, sessions.Count(s => s.WorkingDirectory == "C:/a"));
        Assert.AreEqual(1, sessions.Count(s => s.WorkingDirectory == "C:/b"));
        Assert.IsTrue(sessions.All(s => s.Window.IsZero), "replay procs are windowless");
    }

    [TestMethod]
    public void Parse_NullOrEmpty_YieldsNothing()
    {
        Assert.AreEqual(0, EnvironmentProcessLocator.Parse(null).Count);
        Assert.AreEqual(0, EnvironmentProcessLocator.Parse("").Count);
    }
}
