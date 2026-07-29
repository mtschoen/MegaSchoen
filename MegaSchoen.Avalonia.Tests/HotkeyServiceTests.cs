using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DisplayManager.Core;
using DisplayManager.Core.Models;
using MegaSchoen.Avalonia;

namespace MegaSchoen.Avalonia.Tests;

[TestClass]
public sealed class HotkeyServiceTests
{
    [TestMethod]
    public void EnabledProfileHotkeyRegistersAndDispatchesProfileId()
    {
        var registrar = new FakeHotkeyRegistrar();
        using var service = new HotkeyService(registrar);
        var profile = Profile("Desk", "D", ["Control", "Alt"]);
        var triggeredProfile = Guid.Empty;
        service.Triggered += (_, profileId) => triggeredProfile = profileId;

        service.Refresh([profile]);
        registrar.PressOnlyRegistration();

        Assert.AreEqual(profile.Id, triggeredProfile);
        Assert.AreEqual(0x4003u, registrar.OnlyRegistration.Modifiers);
        Assert.AreEqual((uint)'D', registrar.OnlyRegistration.VirtualKey);
    }

    [TestMethod]
    public void RefreshUnregistersPreviousProfileHotkeys()
    {
        var registrar = new FakeHotkeyRegistrar();
        using var service = new HotkeyService(registrar);
        service.Refresh([Profile("Desk", "D", ["Control"])]);
        var originalId = registrar.OnlyRegistration.Id;

        service.Refresh([Profile("Projector", "F2", ["Alt"])]);

        CollectionAssert.Contains(registrar.UnregisteredIds, originalId);
        Assert.AreEqual(0x71u, registrar.OnlyRegistration.VirtualKey);
    }

    [TestMethod]
    public void DisabledAndInvalidHotkeysAreNotRegistered()
    {
        var registrar = new FakeHotkeyRegistrar();
        using var service = new HotkeyService(registrar);
        var disabled = Profile("Disabled", "D", ["Control"]);
        disabled.Hotkey!.Enabled = false;

        service.Refresh([disabled, Profile("Invalid", "NotAKey", ["Control"])]);

        Assert.AreEqual(0, registrar.RegistrationCount);
    }

    [TestMethod]
    public void RegistrationFailureDoesNotDispatchProfile()
    {
        var registrar = new FakeHotkeyRegistrar { RegistrationSucceeds = false };
        using var service = new HotkeyService(registrar);
        var triggered = false;
        service.Triggered += (_, _) => triggered = true;

        service.Refresh([Profile("Desk", "D", ["Control"])]);
        registrar.Press(1);

        Assert.IsFalse(triggered);
    }

    [TestMethod]
    public void GestureSupportsEveryPersistedKeyName()
    {
        var keys = new Dictionary<string, uint>
        {
            ["d"] = 'D',
            ["7"] = '7',
            ["F24"] = 0x87,
            ["Escape"] = 0x1B,
            ["Tab"] = 0x09,
            ["Space"] = 0x20,
            ["Enter"] = 0x0D,
            ["Backspace"] = 0x08,
            ["Delete"] = 0x2E,
            ["Insert"] = 0x2D,
            ["Home"] = 0x24,
            ["End"] = 0x23,
            ["PageUp"] = 0x21,
            ["PageDown"] = 0x22,
            ["Left"] = 0x25,
            ["Up"] = 0x26,
            ["Right"] = 0x27,
            ["Down"] = 0x28,
            ["PrintScreen"] = 0x2C,
            ["Pause"] = 0x13,
            ["NumLock"] = 0x90,
            ["ScrollLock"] = 0x91,
            ["CapsLock"] = 0x14,
            ["Numpad9"] = 0x69,
            ["Multiply"] = 0x6A,
            ["Add"] = 0x6B,
            ["Subtract"] = 0x6D,
            ["Decimal"] = 0x6E,
            ["Divide"] = 0x6F,
            ["-"] = 0xBD,
            ["="] = 0xBB,
            ["["] = 0xDB,
            ["]"] = 0xDD,
            ["\\"] = 0xDC,
            [";"] = 0xBA,
            ["'"] = 0xDE,
            [","] = 0xBC,
            ["."] = 0xBE,
            ["/"] = 0xBF,
            ["`"] = 0xC0
        };

        foreach (var (key, expectedVirtualKey) in keys)
        {
            var created = HotkeyGesture.TryCreate(
                new HotkeyDefinition { Key = key },
                out var modifiers,
                out var virtualKey);

            Assert.IsTrue(created, $"Expected '{key}' to be supported.");
            Assert.AreEqual(0x4000u, modifiers);
            Assert.AreEqual(expectedVirtualKey, virtualKey);
        }
    }

    [TestMethod]
    public void GestureRejectsUnknownModifierAndOutOfRangeKeys()
    {
        var invalidModifier = HotkeyGesture.TryCreate(
            new HotkeyDefinition { Key = "D", Modifiers = ["Hyper"] },
            out _,
            out _);
        var invalidFunctionKey = HotkeyGesture.TryCreate(
            new HotkeyDefinition { Key = "F25", Modifiers = ["Shift", "Win"] },
            out var modifiers,
            out _);

        Assert.IsFalse(invalidModifier);
        Assert.IsFalse(invalidFunctionKey);
        Assert.AreEqual(0x400Cu, modifiers);
    }

    [TestMethod]
    public async Task CoordinatorAppliesProfileSelectedByRegisteredHotkey()
    {
        var registrar = new FakeHotkeyRegistrar();
        using var hotkeys = new HotkeyService(registrar);
        var profile = Profile("Desk", "D", ["Control"]);
        var actions = new FakeDisplayProfileActions([profile]);
        using var coordinator = new DisplayHotkeyCoordinator(hotkeys, actions, _ => { });
        await coordinator.StartAsync();

        registrar.PressOnlyRegistration();

        Assert.AreSame(profile, actions.AppliedProfile);
    }

    [TestMethod]
    public async Task CoordinatorLogsFailedDisplayApply()
    {
        var registrar = new FakeHotkeyRegistrar();
        using var hotkeys = new HotkeyService(registrar);
        var profile = Profile("Desk", "D", ["Control"]);
        var actions = new FakeDisplayProfileActions([profile])
        {
            Result = new ApplyResult { Success = false, Errors = ["native failure"] }
        };
        var messages = new List<string>();
        using var coordinator = new DisplayHotkeyCoordinator(hotkeys, actions, messages.Add);
        await coordinator.StartAsync();

        registrar.PressOnlyRegistration();

        StringAssert.Contains(messages[0], "native failure");
    }

    [TestMethod]
    public async Task CoordinatorIgnoresUnknownRegistrationAndUsesFallbackError()
    {
        var registrar = new FakeHotkeyRegistrar();
        using var hotkeys = new HotkeyService(registrar);
        var profile = Profile("Desk", "D", ["Control"]);
        var actions = new FakeDisplayProfileActions([profile])
        {
            Result = new ApplyResult { Success = false }
        };
        var messages = new List<string>();
        using var coordinator = new DisplayHotkeyCoordinator(hotkeys, actions, messages.Add);
        await coordinator.StartAsync();

        hotkeys.Refresh([Profile("Unknown", "U", ["Control"])]);
        registrar.PressOnlyRegistration();
        hotkeys.Refresh([profile]);
        registrar.PressOnlyRegistration();

        Assert.HasCount(1, messages);
        StringAssert.Contains(messages[0], "was not applied");
    }

    [TestMethod]
    public void HotkeyServiceCanBeDisposedTwice()
    {
        var registrar = new FakeHotkeyRegistrar();
        var service = new HotkeyService(registrar);
        service.Refresh([Profile("Desk", "D", ["Control"])]);

        service.Dispose();
        service.Dispose();

        Assert.AreEqual(0, registrar.RegistrationCount);
    }

    [TestMethod]
    public async Task CoordinatorDoesNotRegisterAfterDisposalDuringLoad()
    {
        var registrar = new FakeHotkeyRegistrar();
        using var hotkeys = new HotkeyService(registrar);
        var actions = new DelayedDisplayProfileActions();
        var coordinator = new DisplayHotkeyCoordinator(hotkeys, actions, _ => { });

        var start = coordinator.StartAsync();
        coordinator.Dispose();
        actions.Complete([Profile("Desk", "D", ["Control"])]);
        await start;

        Assert.AreEqual(0, registrar.RegistrationCount);
    }

    static SavedDisplayProfile Profile(string name, string key, List<string> modifiers) => new()
    {
        Name = name,
        Hotkey = new HotkeyDefinition
        {
            Key = key,
            Modifiers = modifiers
        }
    };

    sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
    {
        readonly Dictionary<int, Registration> _registrations = [];

        public event EventHandler<int>? Pressed;

        public List<int> UnregisteredIds { get; } = [];

        public bool RegistrationSucceeds { get; set; } = true;

        public int RegistrationCount => _registrations.Count;

        public Registration OnlyRegistration
        {
            get
            {
                Assert.HasCount(1, _registrations);
                foreach (var registration in _registrations.Values)
                {
                    return registration;
                }

                throw new AssertFailedException("No hotkey registration was present.");
            }
        }

        public bool Register(int id, uint modifiers, uint virtualKey)
        {
            if (RegistrationSucceeds)
            {
                _registrations[id] = new Registration(id, modifiers, virtualKey);
            }

            return RegistrationSucceeds;
        }

        public void Unregister(int id)
        {
            _registrations.Remove(id);
            UnregisteredIds.Add(id);
        }

        public void PressOnlyRegistration()
        {
            Pressed?.Invoke(this, OnlyRegistration.Id);
        }

        public void Press(int id)
        {
            Pressed?.Invoke(this, id);
        }

        public sealed record Registration(int Id, uint Modifiers, uint VirtualKey);
    }

    sealed class FakeDisplayProfileActions : IDisplayProfileActions
    {
        readonly List<SavedDisplayProfile> _profiles;

        public FakeDisplayProfileActions(List<SavedDisplayProfile> profiles)
        {
            _profiles = profiles;
        }

        public ApplyResult Result { get; set; } = new() { Success = true };

        public SavedDisplayProfile? AppliedProfile { get; private set; }

        public Task<List<SavedDisplayProfile>> LoadAsync() => Task.FromResult(_profiles);

        public ApplyResult Apply(SavedDisplayProfile profile)
        {
            AppliedProfile = profile;
            return Result;
        }
    }

    sealed class DelayedDisplayProfileActions : IDisplayProfileActions
    {
        readonly TaskCompletionSource<List<SavedDisplayProfile>> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<List<SavedDisplayProfile>> LoadAsync() => _completion.Task;

        public ApplyResult Apply(SavedDisplayProfile profile) =>
            throw new InvalidOperationException("No profile should be applied.");

        public void Complete(List<SavedDisplayProfile> profiles)
        {
            _completion.SetResult(profiles);
        }
    }
}
