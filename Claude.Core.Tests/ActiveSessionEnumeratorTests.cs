using Claude.Core;
using Claude.Core.Models;
using Claude.Core.Tests.Fakes;

namespace Claude.Core.Tests;

[TestClass]
public class ActiveSessionEnumeratorTests
{
    [TestMethod]
    public void Enumerate_NoWindows_ReturnsEmpty()
    {
        using var fixture = new ClaudeProjectsFixture();
        var locator = new FakeProcessLocator();
        var store = new StateStore(Path.Combine(fixture.Root, "state.json"));

        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);

        var result = enumerator.Enumerate();
        Assert.AreEqual(0, result.Count);
    }
}
