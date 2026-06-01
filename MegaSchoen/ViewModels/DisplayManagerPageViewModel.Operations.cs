using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DisplayManager.Core.Models;
using DisplayManager.Core.Services;
#if WINDOWS
using MegaSchoen.Platforms.Windows.Services;
#endif

namespace MegaSchoen.ViewModels;

// Data operations (load/save/apply/delete/refresh) for the Display Manager
// page, split from the main view model file to keep each file focused.
public partial class DisplayManagerPageViewModel
{
    async Task LoadDisplaysAsync()
    {
        try
        {
            var displays = DisplayManager.Core.DisplayManager.GetAllDisplays();

            if (HideInactiveDisplays)
            {
                displays = displays.Where(d => d.IsActive).ToList();
            }

            CurrentDisplays.Clear();
            foreach (var display in displays)
            {
                CurrentDisplays.Add(display);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Failed to load displays: {ex.Message}");
        }
    }

    async Task LoadProfilesAsync()
    {
        try
        {
            var profiles = await _profileService.GetAllProfilesAsync();

            SavedProfiles.Clear();
            foreach (var profile in profiles.OrderByDescending(p => p.LastModified))
            {
                SavedProfiles.Add(profile);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Failed to load profiles: {ex.Message}");
        }
    }

    async Task SaveCurrentArrangementAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName))
        {
            return;
        }

        try
        {
            IsLoading = true;

            var name = NewProfileName.Trim();
            var profiles = await _profileService.GetAllProfilesAsync();
            var existing = profiles.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            var profile = _profileService.CaptureCurrentConfiguration(
                name,
                $"Captured on {DateTime.Now:g}"
            );

            // Preserve the ID and hotkey of an existing profile so we overwrite rather than duplicate
            if (existing != null)
            {
                profile.Id = existing.Id;
                profile.Hotkey = existing.Hotkey;
                profile.Created = existing.Created;
            }

            await _profileService.SaveProfileAsync(profile);

            NewProfileName = "";
            await LoadProfilesAsync();

#if WINDOWS
            RefreshGlobalHotkeys();
#endif

            await ShowSuccessAsync($"Profile '{profile.Name}' saved successfully!");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Failed to save profile: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    async Task DeleteProfileAsync(SavedDisplayProfile? profile)
    {
        if (profile == null)
        {
            return;
        }

        try
        {
            var confirmed = await ConfirmAsync(
                $"Delete Profile",
                $"Are you sure you want to delete '{profile.Name}'?"
            );

            if (!confirmed)
            {
                return;
            }

            IsLoading = true;

            await _profileService.DeleteProfileAsync(profile.Id);
            await LoadProfilesAsync();

#if WINDOWS
            RefreshGlobalHotkeys();
#endif

            await ShowSuccessAsync($"Profile '{profile.Name}' deleted successfully!");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Failed to delete profile: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    async Task ApplyProfileAsync(SavedDisplayProfile? profile)
    {
        if (profile == null)
        {
            return;
        }

        try
        {
            IsLoading = true;

            var result = _profileService.ApplyProfile(profile);

            if (result.Success)
            {
                await ShowSuccessAsync($"Profile '{profile.Name}' applied successfully!");

                // Refresh displays to show the new configuration
                await Task.Delay(500); // Small delay to let Windows settle
                await LoadDisplaysAsync();
            }
            else
            {
                var errors = string.Join("\n", result.Errors);
                await ShowErrorAsync($"Failed to apply profile '{profile.Name}':\n{errors}");
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Failed to apply profile: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    async Task OverwriteProfileAsync(SavedDisplayProfile? profile)
    {
        if (profile == null)
        {
            return;
        }

        try
        {
            var confirmed = await ConfirmAsync(
                "Overwrite Profile",
                $"Overwrite '{profile.Name}' with the current display configuration?"
            );

            if (!confirmed)
            {
                return;
            }

            IsLoading = true;

            // Capture current configuration but keep the existing profile's ID and name
            var updatedProfile = _profileService.CaptureCurrentConfiguration(profile.Name);
            updatedProfile.Id = profile.Id;
            updatedProfile.Created = profile.Created;
            updatedProfile.Description = $"Updated on {DateTime.Now:g}";
            updatedProfile.Hotkey = profile.Hotkey; // Preserve hotkey

            await _profileService.SaveProfileAsync(updatedProfile);
            await LoadProfilesAsync();

            await ShowSuccessAsync($"Profile '{profile.Name}' updated with current configuration!");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Failed to overwrite profile: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    async Task RefreshAllAsync()
    {
        IsLoading = true;
        try
        {
            await LoadDisplaysAsync();
            await LoadProfilesAsync();

#if WINDOWS
            RefreshGlobalHotkeys();
#endif
        }
        finally
        {
            IsLoading = false;
        }
    }

}
