using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DisplayManager.Core.Models;

namespace MegaSchoen.Avalonia.ViewModels;

public sealed partial class DisplayManagerViewModel
{
    public async Task InitializeAsync()
    {
        IsBusy = true;
        ClearStatus();
        try
        {
            try
            {
                allDisplays = service.GetDisplays();
                RebuildDisplays();
            }
            catch (Exception exception)
            {
                SetError($"Could not load displays: {exception.Message}");
            }

            try
            {
                await ReloadProfilesAsync();
            }
            catch (Exception exception)
            {
                SetError($"Could not load display profiles: {exception.Message}");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveCurrentArrangementAsync()
    {
        var name = NewProfileName.Trim();
        if (name.Length == 0)
        {
            SetError("Enter a profile name first.");
            return;
        }

        await RunBusyAsync($"Could not save '{name}'", async () =>
        {
            var existing = SavedProfiles
                .Select(card => card.Profile)
                .FirstOrDefault(profile =>
                    string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));
            var profile = service.CaptureCurrentConfiguration(
                name,
                $"Captured on {DateTime.Now:g}");

            if (existing is not null)
            {
                profile.Id = existing.Id;
                profile.Created = existing.Created;
                profile.Hotkey = existing.Hotkey;
            }

            await service.SaveProfileAsync(profile);
            await refreshRuntimeHotkeys();
            NewProfileName = "";
            await ReloadProfilesAsync();
            SetSuccess($"Profile '{name}' saved.");
        });
    }

    public async Task ApplyProfileAsync(DisplayProfileCardViewModel card)
    {
        await RunBusyAsync($"Could not apply '{card.Name}'", async () =>
        {
            var result = service.ApplyProfile(card.Profile);
            if (!result.Success)
            {
                var errors = result.Errors.Count == 0
                    ? "The native display service returned an unknown error."
                    : string.Join("; ", result.Errors);
                SetError($"Could not apply '{card.Name}': {errors}");
                return;
            }

            allDisplays = service.GetDisplays();
            RebuildDisplays();
            SetSuccess($"Profile '{card.Name}' applied.");
            await Task.CompletedTask;
        });
    }

    public void RequestDelete(DisplayProfileCardViewModel card)
    {
        CancelAllPending();
        card.IsDeleteConfirmationVisible = true;
    }

    public async Task ConfirmDeleteAsync(DisplayProfileCardViewModel card)
    {
        await RunBusyAsync($"Could not delete '{card.Name}'", async () =>
        {
            await service.DeleteProfileAsync(card.Profile.Id);
            await refreshRuntimeHotkeys();
            await ReloadProfilesAsync();
            SetSuccess($"Profile '{card.Name}' deleted.");
        });
    }

    public void RequestOverwrite(DisplayProfileCardViewModel card)
    {
        CancelAllPending();
        card.IsOverwriteConfirmationVisible = true;
    }

    public async Task ConfirmOverwriteAsync(DisplayProfileCardViewModel card)
    {
        await RunBusyAsync($"Could not overwrite '{card.Name}'", async () =>
        {
            var profile = service.CaptureCurrentConfiguration(
                card.Name,
                $"Updated on {DateTime.Now:g}");
            profile.Id = card.Profile.Id;
            profile.Created = card.Profile.Created;
            profile.Hotkey = card.Profile.Hotkey;

            await service.SaveProfileAsync(profile);
            await refreshRuntimeHotkeys();
            await ReloadProfilesAsync();
            SetSuccess($"Profile '{card.Name}' overwritten.");
        });
    }

    public void BeginHotkeyCapture(DisplayProfileCardViewModel card)
    {
        foreach (var profile in SavedProfiles)
        {
            profile.CancelHotkeyCapture();
        }

        card.BeginHotkeyCapture();
        SetSuccess($"Press a shortcut for '{card.Name}', or Escape to cancel.");
    }

    public async Task AssignHotkeyAsync(
        DisplayProfileCardViewModel card,
        string key,
        IReadOnlyList<string> modifiers)
    {
        if (!card.IsCapturingHotkey)
        {
            return;
        }

        await RunBusyAsync($"Could not set hotkey for '{card.Name}'", async () =>
        {
            card.Profile.Hotkey = new HotkeyDefinition
            {
                Key = key,
                Modifiers = modifiers.ToList()
            };
            await service.SaveProfileAsync(card.Profile);
            await refreshRuntimeHotkeys();
            await ReloadProfilesAsync();
            SetSuccess($"Hotkey set for '{card.Name}'.");
        });
    }

    public async Task ClearHotkeyAsync(DisplayProfileCardViewModel card)
    {
        await RunBusyAsync($"Could not clear hotkey for '{card.Name}'", async () =>
        {
            card.Profile.Hotkey = null;
            await service.SaveProfileAsync(card.Profile);
            await refreshRuntimeHotkeys();
            await ReloadProfilesAsync();
            SetSuccess($"Hotkey cleared for '{card.Name}'.");
        });
    }

    public void CancelPending(DisplayProfileCardViewModel card)
    {
        card.IsDeleteConfirmationVisible = false;
        card.IsOverwriteConfirmationVisible = false;
        card.CancelHotkeyCapture();
    }

    async Task RunBusyAsync(string failurePrefix, Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            SetError($"{failurePrefix}: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
