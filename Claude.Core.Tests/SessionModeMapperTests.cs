using Claude.Core.Models;

namespace Claude.Core.Tests;

[TestClass]
public class SessionModeMapperTests
{
    [TestMethod]
    [DataRow("plan", SessionMode.Plan)]
    [DataRow("auto", SessionMode.Auto)]
    [DataRow("default", SessionMode.Build)]
    [DataRow("acceptEdits", SessionMode.Build)]
    [DataRow("dontAsk", SessionMode.Build)]
    [DataRow("bypassPermissions", SessionMode.Build)]
    [DataRow(null, SessionMode.Unknown)]
    [DataRow("future", SessionMode.Unknown)]
    public void FromPermissionMode_MapsRawValue(string? raw, SessionMode expected)
    {
        Assert.AreEqual(expected, SessionModeMapper.FromPermissionMode(raw));
    }
}
