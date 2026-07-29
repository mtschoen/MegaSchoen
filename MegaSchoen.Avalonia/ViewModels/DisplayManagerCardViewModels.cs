using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using DisplayManager.Core;
using DisplayManager.Core.Models;

namespace MegaSchoen.Avalonia.ViewModels;

public sealed class DisplayCardViewModel(DisplayInfo display)
{
    public string MonitorName => display.MonitorName;
    public string DeviceName => display.DeviceName;
    public bool IsActive => display.IsActive;
    public bool IsPrimary =>
        display.IsActive && display.PositionX == 0 && display.PositionY == 0;
    public string ModeText =>
        $"{display.Width} × {display.Height} @ {display.RefreshRate:0} Hz";
}

public sealed class DisplayProfileCardViewModel : INotifyPropertyChanged
{
    bool isCapturingHotkey;
    bool isDeleteConfirmationVisible;
    bool isOverwriteConfirmationVisible;

    internal DisplayProfileCardViewModel(SavedDisplayProfile profile)
    {
        Profile = profile;
    }

    internal SavedDisplayProfile Profile { get; }

    public string Name => Profile.Name;
    public string Description => Profile.Description;
    public int DisplayCount => Profile.Displays.Count;
    public DateTime CreatedLocal => Profile.Created.ToLocalTime();
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasHotkey => Profile.Hotkey is { Enabled: true, Key.Length: > 0 };
    public bool IsCapturingHotkey => isCapturingHotkey;
    public string HotkeyButtonText =>
        isCapturingHotkey ? "Press shortcut…" : FormatHotkey(Profile.Hotkey);

    public bool IsDeleteConfirmationVisible
    {
        get => isDeleteConfirmationVisible;
        internal set
        {
            if (isDeleteConfirmationVisible == value)
            {
                return;
            }

            isDeleteConfirmationVisible = value;
            OnPropertyChanged();
        }
    }

    public bool IsOverwriteConfirmationVisible
    {
        get => isOverwriteConfirmationVisible;
        internal set
        {
            if (isOverwriteConfirmationVisible == value)
            {
                return;
            }

            isOverwriteConfirmationVisible = value;
            OnPropertyChanged();
        }
    }

    internal void BeginHotkeyCapture()
    {
        isCapturingHotkey = true;
        OnPropertyChanged(nameof(IsCapturingHotkey));
        OnPropertyChanged(nameof(HotkeyButtonText));
    }

    internal void CancelHotkeyCapture()
    {
        isCapturingHotkey = false;
        OnPropertyChanged(nameof(IsCapturingHotkey));
        OnPropertyChanged(nameof(HotkeyButtonText));
    }

    internal static string FormatHotkey(HotkeyDefinition? hotkey)
    {
        if (hotkey is not { Enabled: true, Key.Length: > 0 })
        {
            return "Set hotkey";
        }

        var parts = hotkey.Modifiers
            .Select(modifier => modifier == "Control" ? "Ctrl" : modifier)
            .Append(hotkey.Key);
        return string.Join("+", parts);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
