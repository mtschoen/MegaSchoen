using Claude.Core.Models;

namespace Claude.Core.Tests;

[TestClass]
public class NullWindowServicesTests
{
    [TestMethod]
    public void NullFocuser_AnyWindow_ReturnsFalse()
    {
        Assert.IsFalse(new NullClaudeWindowFocuser().BringToFront(WindowToken.Null));
    }

    [TestMethod]
    public void NullSshResolver_AnyPort_ReturnsNull()
    {
        Assert.IsNull(new NullSshSessionWindowResolver().ResolveWindow(12345));
    }
}
