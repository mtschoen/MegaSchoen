using DisplayManager.Core.Models;
using static MegaSchoen.Platforms.Windows.Services.Win32Interop;

namespace MegaSchoen.Platforms.Windows.Services;

/// <summary>
/// Manages global hotkey registration using the Win32 RegisterHotKey API.
/// </summary>
sealed class GlobalHotkeyService : IDisposable
{
    readonly MessageWindow _messageWindow;
    readonly Dictionary<int, Guid> _hotkeyToProfile = new();
    int _nextHotkeyId = 1;
    bool _disposed;

    /// <summary>
    /// Fired when a registered hotkey is pressed. Parameter is the profile ID.
    /// </summary>
    public event EventHandler<Guid>? HotkeyTriggered;

    public GlobalHotkeyService(MessageWindow messageWindow)
    {
        _messageWindow = messageWindow;
        _messageWindow.HotkeyPressed += OnHotkeyPressed;
    }

    /// <summary>
    /// Registers all hotkeys from the given profiles.
    /// </summary>
    public void RefreshFromProfiles(List<SavedDisplayProfile> profiles)
    {
        UnregisterAll();

        foreach (var profile in profiles)
        {
            if (profile.Hotkey?.Enabled == true && !string.IsNullOrEmpty(profile.Hotkey.Key))
            {
                RegisterHotkey(profile.Id, profile.Hotkey);
            }
        }
    }

    /// <summary>
    /// Registers a single hotkey for a profile.
    /// </summary>
    public bool RegisterHotkey(Guid profileId, HotkeyDefinition hotkey)
    {
        if (!hotkey.Enabled || string.IsNullOrEmpty(hotkey.Key))
        {
            return false;
        }

        var vk = KeyToVirtualKey(hotkey.Key);
        if (vk == 0)
        {
            System.Diagnostics.Debug.WriteLine($"Unknown key: {hotkey.Key}");
            return false;
        }

        uint modifiers = MOD_NOREPEAT;
        foreach (var mod in hotkey.Modifiers)
        {
            modifiers |= ModifierToFlag(mod);
        }

        var hotkeyId = _nextHotkeyId++;
        var success = RegisterHotKey(_messageWindow.Handle, hotkeyId, modifiers, vk);

        if (success)
        {
            _hotkeyToProfile[hotkeyId] = profileId;
            System.Diagnostics.Debug.WriteLine($"Registered hotkey {hotkeyId} for profile {profileId}");
        }
        else
        {
            var error = GetLastError();
            System.Diagnostics.Debug.WriteLine($"Failed to register hotkey for profile {profileId}: error {error}");
        }

        return success;
    }

    /// <summary>
    /// Unregisters all hotkeys.
    /// </summary>
    public void UnregisterAll()
    {
        foreach (var hotkeyId in _hotkeyToProfile.Keys.ToList())
        {
            UnregisterHotKey(_messageWindow.Handle, hotkeyId);
        }
        _hotkeyToProfile.Clear();
    }

    void OnHotkeyPressed(object? sender, int hotkeyId)
    {
        if (_hotkeyToProfile.TryGetValue(hotkeyId, out var profileId))
        {
            System.Diagnostics.Debug.WriteLine($"Hotkey {hotkeyId} pressed, triggering profile {profileId}");
            HotkeyTriggered?.Invoke(this, profileId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        _messageWindow.HotkeyPressed -= OnHotkeyPressed;
    }
}
