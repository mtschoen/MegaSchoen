using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DisplayManager.Core;
using DisplayManager.Core.Models;
using DisplayManager.Core.Services;

namespace MegaSchoen.Avalonia;

sealed class DisplayHotkeyCoordinator : IDisposable
{
    readonly HotkeyService _hotkeys;
    readonly IDisplayProfileActions _profiles;
    readonly Action<string> _log;
    Dictionary<Guid, SavedDisplayProfile> _profilesById = [];
    bool _disposed;

    public DisplayHotkeyCoordinator(
        HotkeyService hotkeys,
        IDisplayProfileActions profiles,
        Action<string> log)
    {
        _hotkeys = hotkeys;
        _profiles = profiles;
        _log = log;
        _hotkeys.Triggered += OnTriggered;
    }

    public async Task StartAsync()
    {
        var profiles = await _profiles.LoadAsync();
        if (_disposed)
        {
            return;
        }

        _profilesById = profiles.ToDictionary(profile => profile.Id);
        _hotkeys.Refresh(profiles);
    }

    public void Dispose()
    {
        _disposed = true;
        _hotkeys.Triggered -= OnTriggered;
    }

    void OnTriggered(object? sender, Guid profileId)
    {
        if (!_profilesById.TryGetValue(profileId, out var profile))
        {
            return;
        }

        var result = _profiles.Apply(profile);
        if (result.Success)
        {
            _log($"Display profile '{profile.Name}' applied via hotkey.");
            return;
        }

        var detail = result.Errors.FirstOrDefault() ?? "The display configuration was not applied.";
        _log($"Display profile '{profile.Name}' failed via hotkey: {detail}");
    }
}

interface IDisplayProfileActions
{
    Task<List<SavedDisplayProfile>> LoadAsync();

    ApplyResult Apply(SavedDisplayProfile profile);
}

sealed class DisplayProfileActions : IDisplayProfileActions
{
    readonly DisplayProfileService _profiles = new();

    public Task<List<SavedDisplayProfile>> LoadAsync() => _profiles.GetAllProfilesAsync();

    public ApplyResult Apply(SavedDisplayProfile profile) => _profiles.ApplyProfile(profile);
}
