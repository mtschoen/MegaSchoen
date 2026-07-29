using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DisplayManager.Core;
using DisplayManager.Core.Models;
using MegaSchoen.Avalonia.Services;
using MegaSchoen.Avalonia.ViewModels;

namespace MegaSchoen.Avalonia.Tests;

[TestClass]
public sealed class DisplayManagerViewModelTests
{
    static readonly string[] ExpectedProfileOrder = ["Newer", "Older"];

    [TestMethod]
    public async Task InitializeFiltersInactiveDisplaysAndSortsProfiles()
    {
        var older = Profile("Older", modifiedMinutesAgo: 10);
        var newer = Profile("Newer", modifiedMinutesAgo: 1);
        var service = new FakeDisplayManagerService
        {
            Displays =
            [
                new DisplayInfo
                {
                    MonitorName = "Primary",
                    IsActive = true,
                    PositionX = 0,
                    PositionY = 0
                },
                new DisplayInfo
                {
                    MonitorName = "Disconnected",
                    IsActive = false
                }
            ],
            Profiles = [older, newer]
        };
        var viewModel = new DisplayManagerViewModel(service);

        await viewModel.InitializeAsync();

        Assert.HasCount(1, viewModel.CurrentDisplays);
        Assert.IsTrue(viewModel.CurrentDisplays[0].IsPrimary);
        CollectionAssert.AreEqual(
            ExpectedProfileOrder,
            viewModel.SavedProfiles.Select(profile => profile.Name).ToArray());
        Assert.IsFalse(viewModel.HasNoDisplays);
        Assert.IsFalse(viewModel.HasNoProfiles);

        viewModel.HideInactiveDisplays = false;

        Assert.HasCount(2, viewModel.CurrentDisplays);
    }

    [TestMethod]
    public async Task SaveExistingNamePreservesIdentityAndHotkey()
    {
        var created = DateTime.UtcNow.AddDays(-5);
        var existing = Profile("Desk");
        existing.Created = created;
        existing.Hotkey = Hotkey("D");
        var service = new FakeDisplayManagerService
        {
            Profiles = [existing]
        };
        var viewModel = new DisplayManagerViewModel(service)
        {
            NewProfileName = "  desk  "
        };
        await viewModel.InitializeAsync();

        await viewModel.SaveCurrentArrangementAsync();

        Assert.HasCount(1, service.SavedProfiles);
        var saved = service.SavedProfiles[0];
        Assert.AreEqual(existing.Id, saved.Id);
        Assert.AreEqual(created, saved.Created);
        Assert.AreSame(existing.Hotkey, saved.Hotkey);
        Assert.AreEqual("desk", saved.Name);
        Assert.AreEqual("", viewModel.NewProfileName);
        Assert.AreEqual("Profile 'desk' saved.", viewModel.StatusMessage);
        Assert.IsFalse(viewModel.IsError);
    }

    [TestMethod]
    public async Task ApplyFailureIsShownInline()
    {
        var profile = Profile("Projector");
        var service = new FakeDisplayManagerService
        {
            Profiles = [profile],
            ApplyResult = new ApplyResult
            {
                Success = false,
                Errors = ["SetDisplayConfig failed"]
            }
        };
        var viewModel = new DisplayManagerViewModel(service);
        await viewModel.InitializeAsync();

        await viewModel.ApplyProfileAsync(viewModel.SavedProfiles[0]);

        Assert.IsTrue(viewModel.IsError);
        Assert.AreEqual(
            "Could not apply 'Projector': SetDisplayConfig failed",
            viewModel.StatusMessage);
    }

    [TestMethod]
    public async Task DeleteRequiresInlineConfirmation()
    {
        var profile = Profile("Old");
        var service = new FakeDisplayManagerService
        {
            Profiles = [profile]
        };
        var viewModel = new DisplayManagerViewModel(service);
        await viewModel.InitializeAsync();
        var card = viewModel.SavedProfiles[0];

        viewModel.RequestDelete(card);

        Assert.IsTrue(card.IsDeleteConfirmationVisible);
        Assert.HasCount(0, service.DeletedProfileIds);

        await viewModel.ConfirmDeleteAsync(card);

        CollectionAssert.AreEqual(new[] { profile.Id }, service.DeletedProfileIds);
        Assert.IsTrue(viewModel.HasNoProfiles);
        Assert.AreEqual("Profile 'Old' deleted.", viewModel.StatusMessage);
    }

    [TestMethod]
    public async Task OverwritePreservesIdentityAndHotkey()
    {
        var created = DateTime.UtcNow.AddDays(-2);
        var profile = Profile("Gaming");
        profile.Created = created;
        profile.Hotkey = Hotkey("G");
        var service = new FakeDisplayManagerService
        {
            Profiles = [profile]
        };
        var viewModel = new DisplayManagerViewModel(service);
        await viewModel.InitializeAsync();
        var card = viewModel.SavedProfiles[0];

        viewModel.RequestOverwrite(card);
        Assert.IsTrue(card.IsOverwriteConfirmationVisible);

        await viewModel.ConfirmOverwriteAsync(card);

        var saved = service.SavedProfiles.Single();
        Assert.AreEqual(profile.Id, saved.Id);
        Assert.AreEqual(created, saved.Created);
        Assert.AreSame(profile.Hotkey, saved.Hotkey);
        Assert.AreEqual("Gaming", saved.Name);
        Assert.AreEqual("Profile 'Gaming' overwritten.", viewModel.StatusMessage);
    }

    [TestMethod]
    public async Task HotkeyAssignmentAndClearArePersisted()
    {
        var profile = Profile("Desk");
        var service = new FakeDisplayManagerService
        {
            Profiles = [profile]
        };
        var viewModel = new DisplayManagerViewModel(service);
        await viewModel.InitializeAsync();
        var card = viewModel.SavedProfiles[0];

        viewModel.BeginHotkeyCapture(card);
        Assert.AreEqual("Press shortcut…", card.HotkeyButtonText);

        await viewModel.AssignHotkeyAsync(card, "D", ["Control", "Alt"]);

        Assert.AreEqual("Ctrl+Alt+D", viewModel.SavedProfiles[0].HotkeyButtonText);
        Assert.IsTrue(viewModel.SavedProfiles[0].HasHotkey);

        await viewModel.ClearHotkeyAsync(viewModel.SavedProfiles[0]);

        Assert.AreEqual("Set hotkey", viewModel.SavedProfiles[0].HotkeyButtonText);
        Assert.IsFalse(viewModel.SavedProfiles[0].HasHotkey);
    }

    [TestMethod]
    public async Task HotkeyAssignmentIsIgnoredUntilCaptureStarts()
    {
        var profile = Profile("Desk");
        var service = new FakeDisplayManagerService
        {
            Profiles = [profile]
        };
        var viewModel = new DisplayManagerViewModel(service);
        await viewModel.InitializeAsync();

        await viewModel.AssignHotkeyAsync(viewModel.SavedProfiles[0], "D", ["Control"]);

        Assert.HasCount(0, service.SavedProfiles);
        Assert.IsNull(profile.Hotkey);
    }

    [TestMethod]
    public async Task ProfileMutationsRefreshRuntimeHotkeys()
    {
        var service = new FakeDisplayManagerService
        {
            Profiles = [Profile("Desk")]
        };
        var refreshCount = 0;
        Func<Task> refreshHotkeys = () =>
        {
            refreshCount++;
            return Task.CompletedTask;
        };
        var viewModel = new DisplayManagerViewModel(service, refreshHotkeys);
        await viewModel.InitializeAsync();

        viewModel.NewProfileName = "Projector";
        await viewModel.SaveCurrentArrangementAsync();
        Assert.AreEqual(1, refreshCount);

        var desk = viewModel.SavedProfiles.Single(profile => profile.Name == "Desk");
        await viewModel.ConfirmDeleteAsync(desk);
        Assert.AreEqual(2, refreshCount);

        var projector = viewModel.SavedProfiles.Single();
        await viewModel.ConfirmOverwriteAsync(projector);
        Assert.AreEqual(3, refreshCount);

        projector = viewModel.SavedProfiles.Single();
        viewModel.BeginHotkeyCapture(projector);
        await viewModel.AssignHotkeyAsync(projector, "P", ["Control"]);
        Assert.AreEqual(4, refreshCount);

        projector = viewModel.SavedProfiles.Single();
        await viewModel.ClearHotkeyAsync(projector);
        Assert.AreEqual(5, refreshCount);
    }

    [TestMethod]
    public async Task RefreshFailureLeavesBusyStateAndSurfacesError()
    {
        var service = new FakeDisplayManagerService
        {
            LoadException = new IOException("config locked")
        };
        var viewModel = new DisplayManagerViewModel(service);

        await viewModel.InitializeAsync();

        Assert.IsFalse(viewModel.IsBusy);
        Assert.IsTrue(viewModel.IsError);
        Assert.AreEqual("Could not load display profiles: config locked", viewModel.StatusMessage);
    }

    [TestMethod]
    public async Task SuccessfulRefreshClearsPriorError()
    {
        var service = new FakeDisplayManagerService
        {
            LoadException = new IOException("config locked")
        };
        var viewModel = new DisplayManagerViewModel(service);
        await viewModel.InitializeAsync();
        service.LoadException = null;

        await viewModel.InitializeAsync();

        Assert.IsFalse(viewModel.IsError);
        Assert.IsFalse(viewModel.HasStatus);
    }

    static SavedDisplayProfile Profile(string name, int modifiedMinutesAgo = 0) => new()
    {
        Name = name,
        LastModified = DateTime.UtcNow.AddMinutes(-modifiedMinutesAgo)
    };

    static HotkeyDefinition Hotkey(string key) => new()
    {
        Key = key,
        Modifiers = ["Control", "Alt"]
    };

    sealed class FakeDisplayManagerService : IDisplayManagerService
    {
        public List<DisplayInfo> Displays { get; set; } = [];
        public List<SavedDisplayProfile> Profiles { get; set; } = [];
        public List<SavedDisplayProfile> SavedProfiles { get; } = [];
        public List<Guid> DeletedProfileIds { get; } = [];
        public ApplyResult ApplyResult { get; set; } = new() { Success = true };
        public Exception? LoadException { get; set; }

        public IReadOnlyList<DisplayInfo> GetDisplays() => Displays;

        public Task<IReadOnlyList<SavedDisplayProfile>> GetProfilesAsync()
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return Task.FromResult<IReadOnlyList<SavedDisplayProfile>>(Profiles);
        }

        public SavedDisplayProfile CaptureCurrentConfiguration(string name, string description) => new()
        {
            Name = name,
            Description = description
        };

        public Task SaveProfileAsync(SavedDisplayProfile profile)
        {
            SavedProfiles.Add(profile);
            Profiles.RemoveAll(candidate => candidate.Id == profile.Id);
            Profiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task DeleteProfileAsync(Guid profileId)
        {
            DeletedProfileIds.Add(profileId);
            Profiles.RemoveAll(profile => profile.Id == profileId);
            return Task.CompletedTask;
        }

        public ApplyResult ApplyProfile(SavedDisplayProfile profile) => ApplyResult;
    }
}
