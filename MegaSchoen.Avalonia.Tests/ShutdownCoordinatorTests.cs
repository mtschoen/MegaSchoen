using MegaSchoen.Avalonia;

namespace MegaSchoen.Avalonia.Tests;

[TestClass]
public sealed class ShutdownCoordinatorTests
{
    [TestMethod]
    public void WindowCloseIsHiddenBeforeShutdownIsRequested()
    {
        var coordinator = new ShutdownCoordinator();

        Assert.IsTrue(coordinator.ShouldHideWindow);
    }

    [TestMethod]
    public void WindowCloseContinuesAfterShutdownIsRequested()
    {
        var coordinator = new ShutdownCoordinator();
        coordinator.RequestShutdown();

        Assert.IsFalse(coordinator.ShouldHideWindow);
    }

    [TestMethod]
    public void ShutdownStateUsesVolatileMemorySemantics()
    {
        var stateField = typeof(ShutdownCoordinator).GetField(
            "_isShuttingDown",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.IsNotNull(stateField);
        CollectionAssert.Contains(
            stateField.GetRequiredCustomModifiers(),
            typeof(System.Runtime.CompilerServices.IsVolatile));
    }
}
