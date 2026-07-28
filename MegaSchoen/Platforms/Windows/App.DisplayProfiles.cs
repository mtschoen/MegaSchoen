using DisplayManager.Core.Models;
using DisplayManager.Core.Services;
using MegaSchoen.Platforms.Windows.Services;
using Microsoft.Win32;

namespace MegaSchoen.WinUI;

public partial class App
{
    static void WireDisplayResumeEvents()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    static void UnwireDisplayResumeEvents()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == PowerModes.Resume)
        {
            DisplayManager.Core.DisplayManager.RecordSystemResume();
        }
    }

    static void WireProfileSelection(TrayIconService tray, List<SavedDisplayProfile> profiles, DisplayProfileService profileService)
    {
        tray.ProfileSelected += (_, profileId) =>
        {
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile is not null)
            {
                ApplyProfile(tray, profileService, profile, "applied successfully.");
            }
        };
    }

    static void WireDisplayHotkeys(
        GlobalHotkeyService hotkeys,
        TrayIconService tray,
        DisplayProfileService profileService,
        List<SavedDisplayProfile> profiles)
    {
        hotkeys.HotkeyTriggered += (_, profileId) =>
        {
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile is not null)
            {
                ApplyProfile(tray, profileService, profile, "applied via hotkey.");
            }
        };
    }

    static void ApplyProfile(
        TrayIconService tray,
        DisplayProfileService profileService,
        SavedDisplayProfile profile,
        string successMessage)
    {
        var result = profileService.ApplyProfile(profile);
        if (result.Success)
        {
            tray.ShowNotification("Profile Applied", $"'{profile.Name}' {successMessage}");
            return;
        }

        var detail = result.Errors.FirstOrDefault() ?? "The display configuration was not applied.";
        var title = result.Deferred ? "Profile Deferred" : "Profile Failed";
        var icon = result.Deferred ? NotificationIcon.Warning : NotificationIcon.Error;
        tray.ShowNotification(title, $"'{profile.Name}' was not applied. {detail}", icon);
    }
}
