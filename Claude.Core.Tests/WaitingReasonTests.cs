using Claude.Core.Models;

namespace Claude.Core.Tests;

[TestClass]
public class WaitingReasonTests
{
    [TestMethod]
    [DataRow(WaitingReason.Permission, true)]
    [DataRow(WaitingReason.AwaitingInput, true)]
    [DataRow(WaitingReason.Working, false)]
    public void IsNeedy_IsTrueOnlyWhenBlockedOnUser(WaitingReason reason, bool expected)
    {
        Assert.AreEqual(expected, reason.IsNeedy());
    }
}
