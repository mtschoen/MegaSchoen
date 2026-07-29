using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DisplayManager.Core;
using DisplayManager.Core.Models;
using DisplayManager.Core.Services;

namespace MegaSchoen.Avalonia.Services;

interface IDisplayManagerService
{
    IReadOnlyList<DisplayInfo> GetDisplays();
    Task<IReadOnlyList<SavedDisplayProfile>> GetProfilesAsync();
    SavedDisplayProfile CaptureCurrentConfiguration(string name, string description);
    Task SaveProfileAsync(SavedDisplayProfile profile);
    Task DeleteProfileAsync(Guid profileId);
    ApplyResult ApplyProfile(SavedDisplayProfile profile);
}

sealed class DisplayManagerService : IDisplayManagerService
{
    readonly DisplayProfileService profileService = new();

    public IReadOnlyList<DisplayInfo> GetDisplays() =>
        DisplayManager.Core.DisplayManager.GetAllDisplays();

    public async Task<IReadOnlyList<SavedDisplayProfile>> GetProfilesAsync() =>
        await profileService.GetAllProfilesAsync();

    public SavedDisplayProfile CaptureCurrentConfiguration(string name, string description) =>
        profileService.CaptureCurrentConfiguration(name, description);

    public Task SaveProfileAsync(SavedDisplayProfile profile) =>
        profileService.SaveProfileAsync(profile);

    public Task DeleteProfileAsync(Guid profileId) =>
        profileService.DeleteProfileAsync(profileId);

    public ApplyResult ApplyProfile(SavedDisplayProfile profile) =>
        profileService.ApplyProfile(profile);
}
