using System;

namespace MegaSchoen.Avalonia.Tests;

[TestClass]
public sealed class AutostartServiceTests
{
    [TestMethod]
    public void WindowsBackendWritesHiddenLaunchCommand()
    {
        var store = new FakeStartupValueStore();
        var backend = new WindowsAutostartBackend(
            store,
            () => @"C:\Program Files\MegaSchoen\MegaSchoen.Avalonia.exe");

        backend.SetEnabled(true);

        Assert.AreEqual(
            "\"C:\\Program Files\\MegaSchoen\\MegaSchoen.Avalonia.exe\" --hidden",
            store.Value);
        Assert.IsTrue(backend.IsEnabled);
    }

    [TestMethod]
    public void WindowsBackendDeletesStartupValueWhenDisabled()
    {
        var store = new FakeStartupValueStore { Value = "existing command" };
        var backend = new WindowsAutostartBackend(store, () => "unused.exe");

        backend.SetEnabled(false);

        Assert.IsNull(store.Value);
        Assert.IsFalse(backend.IsEnabled);
    }

    [TestMethod]
    public void WindowsBackendRejectsMissingExecutablePath()
    {
        var backend = new WindowsAutostartBackend(new FakeStartupValueStore(), () => null);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => backend.SetEnabled(true));

        StringAssert.Contains(exception.Message, "executable path");
    }

    [TestMethod]
    public void ControllerDelegatesToSelectedPlatformBackend()
    {
        var backend = new FakeAutostartBackend();
        var controller = new AutostartController(backend);

        controller.SetEnabled(true);

        Assert.IsTrue(controller.IsEnabled);
        Assert.IsTrue(backend.SetEnabledCalled);
    }

    sealed class FakeStartupValueStore : IStartupValueStore
    {
        public string? Value { get; set; }

        public string? Read() => Value;

        public void Write(string? value)
        {
            Value = value;
        }
    }

    sealed class FakeAutostartBackend : IAutostartBackend
    {
        public bool IsEnabled { get; private set; }

        public bool SetEnabledCalled { get; private set; }

        public void SetEnabled(bool enabled)
        {
            SetEnabledCalled = true;
            IsEnabled = enabled;
        }
    }
}
