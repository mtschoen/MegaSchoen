using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DisplayManager.Core.Models;

namespace MegaSchoen.Avalonia;

sealed class HotkeyService : IDisposable
{
    readonly IHotkeyRegistrar _registrar;
    readonly Dictionary<int, Guid> _hotkeyToProfile = [];
    int _nextId = 1;
    bool _disposed;

    public HotkeyService(IHotkeyRegistrar registrar)
    {
        _registrar = registrar;
        _registrar.Pressed += OnPressed;
    }

    public event EventHandler<Guid>? Triggered;

    public void Refresh(IEnumerable<SavedDisplayProfile> profiles)
    {
        UnregisterAll();

        foreach (var profile in profiles)
        {
            if (profile.Hotkey is not { Enabled: true } hotkey
                || !HotkeyGesture.TryCreate(hotkey, out var modifiers, out var virtualKey))
            {
                continue;
            }

            var id = _nextId++;
            if (_registrar.Register(id, modifiers, virtualKey))
            {
                _hotkeyToProfile[id] = profile.Id;
            }
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
        _registrar.Pressed -= OnPressed;
    }

    void UnregisterAll()
    {
        foreach (var id in _hotkeyToProfile.Keys.ToList())
        {
            _registrar.Unregister(id);
        }

        _hotkeyToProfile.Clear();
    }

    void OnPressed(object? sender, int id)
    {
        if (_hotkeyToProfile.TryGetValue(id, out var profileId))
        {
            Triggered?.Invoke(this, profileId);
        }
    }
}

interface IHotkeyRegistrar
{
    event EventHandler<int>? Pressed;

    bool Register(int id, uint modifiers, uint virtualKey);

    void Unregister(int id);
}

static class HotkeyGesture
{
    const uint NoRepeat = 0x4000;

    public static bool TryCreate(HotkeyDefinition hotkey, out uint modifiers, out uint virtualKey)
    {
        modifiers = NoRepeat;
        foreach (var modifier in hotkey.Modifiers)
        {
            var flag = modifier switch
            {
                "Alt" => 0x0001u,
                "Control" => 0x0002u,
                "Shift" => 0x0004u,
                "Win" => 0x0008u,
                _ => 0u
            };
            if (flag == 0)
            {
                virtualKey = 0;
                return false;
            }

            modifiers |= flag;
        }

        virtualKey = KeyToVirtualKey(hotkey.Key);
        return virtualKey != 0;
    }

    static uint KeyToVirtualKey(string key)
    {
        if (key.Length == 1)
        {
            var character = char.ToUpper(key[0], CultureInfo.InvariantCulture);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                return character;
            }
        }

        if (key.StartsWith('F')
            && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var functionKey)
            && functionKey is >= 1 and <= 24)
        {
            return checked((uint)(0x70 + functionKey - 1));
        }

        return key switch
        {
            "Escape" => 0x1B,
            "Tab" => 0x09,
            "Space" => 0x20,
            "Enter" => 0x0D,
            "Backspace" => 0x08,
            "Delete" => 0x2E,
            "Insert" => 0x2D,
            "Home" => 0x24,
            "End" => 0x23,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            "Left" => 0x25,
            "Up" => 0x26,
            "Right" => 0x27,
            "Down" => 0x28,
            "PrintScreen" => 0x2C,
            "Pause" => 0x13,
            "NumLock" => 0x90,
            "ScrollLock" => 0x91,
            "CapsLock" => 0x14,
            "Multiply" => 0x6A,
            "Add" => 0x6B,
            "Subtract" => 0x6D,
            "Decimal" => 0x6E,
            "Divide" => 0x6F,
            "-" => 0xBD,
            "=" => 0xBB,
            "[" => 0xDB,
            "]" => 0xDD,
            "\\" => 0xDC,
            ";" => 0xBA,
            "'" => 0xDE,
            "," => 0xBC,
            "." => 0xBE,
            "/" => 0xBF,
            "`" => 0xC0,
            _ when key.StartsWith("Numpad", StringComparison.Ordinal)
                && int.TryParse(key.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number is >= 0 and <= 9 => checked((uint)(0x60 + number)),
            _ => 0
        };
    }
}
