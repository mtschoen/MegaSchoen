namespace MegaSchoen.Avalonia.Tests;

[TestClass]
public sealed class SingleInstanceGuardTests
{
    [TestMethod]
    public void PrimaryInstanceContinuesWithoutSignaling()
    {
        var instanceLock = new FakeSingleInstanceLock(true);
        var signalCount = 0;
        using var guard = new SingleInstanceGuard(instanceLock, () => signalCount++);

        var isPrimary = guard.TryAcquire();

        Assert.IsTrue(isPrimary);
        Assert.AreEqual(0, signalCount);
    }

    [TestMethod]
    public void SecondaryInstanceSignalsPrimaryAndStops()
    {
        var instanceLock = new FakeSingleInstanceLock(false);
        var signalCount = 0;
        using var guard = new SingleInstanceGuard(instanceLock, () => signalCount++);

        var isPrimary = guard.TryAcquire();

        Assert.IsFalse(isPrimary);
        Assert.AreEqual(1, signalCount);
    }

    [TestMethod]
    public void GuardDisposesInstanceLock()
    {
        var instanceLock = new FakeSingleInstanceLock(true);
        var guard = new SingleInstanceGuard(instanceLock, () => { });

        guard.Dispose();

        Assert.IsTrue(instanceLock.Disposed);
    }

    sealed class FakeSingleInstanceLock : ISingleInstanceLock
    {
        readonly bool _acquired;

        public FakeSingleInstanceLock(bool acquired)
        {
            _acquired = acquired;
        }

        public bool Disposed { get; private set; }

        public bool TryAcquire() => _acquired;

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
