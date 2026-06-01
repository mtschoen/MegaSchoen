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

public partial class DisplayManagerPageViewModel : INotifyPropertyChanged
{
    readonly DisplayProfileService _profileService;
    bool _isLoading;
    string _newProfileName = "";
    bool _hideInactiveDisplays = true;
    bool _minimizeToTray = true;
    bool _startWithWindows;
    SavedDisplayProfile? _hotkeyCapturingProfile;

#if WINDOWS
    KeyCaptureService? _keyCaptureService;
    GlobalHotkeyService? _globalHotkeyService;
    TrayIconService? _trayIconService;
#endif

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DisplayManager.Core.DisplayInfo> CurrentDisplays { get; } = [];
    public ObservableCollection<SavedDisplayProfile> SavedProfiles { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string NewProfileName
    {
        get => _newProfileName;
        set
        {
            _newProfileName = value;
            OnPropertyChanged();
            ((Command)SaveCurrentArrangementCommand).ChangeCanExecute();
        }
    }

    public bool HideInactiveDisplays
    {
        get => _hideInactiveDisplays;
        set
        {
            _hideInactiveDisplays = value;
            OnPropertyChanged();
            _ = LoadDisplaysAsync();
        }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            _minimizeToTray = value;
            OnPropertyChanged();
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows != value)
            {
                _startWithWindows = value;
                OnPropertyChanged();
#if WINDOWS
                StartupService.SetStartupEnabled(value);
#endif
            }
        }
    }

    public bool IsCapturingHotkey => _hotkeyCapturingProfile != null;
    public Guid? CapturingProfileId => _hotkeyCapturingProfile?.Id;

    public ICommand LoadDisplaysCommand { get; }
    public ICommand SaveCurrentArrangementCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ApplyProfileCommand { get; }
    public ICommand OverwriteProfileCommand { get; }
    public ICommand EditLayoutCommand { get; }
    public ICommand SetHotkeyCommand { get; }
    public ICommand ClearHotkeyCommand { get; }

    public DisplayManagerPageViewModel()
    {
        _profileService = new DisplayProfileService();

        LoadDisplaysCommand = new Command(async () => await LoadDisplaysAsync());
        SaveCurrentArrangementCommand = new Command(
            async () => await SaveCurrentArrangementAsync(),
            () => !string.IsNullOrWhiteSpace(NewProfileName)
        );
        DeleteProfileCommand = new Command<SavedDisplayProfile>(async (profile) => await DeleteProfileAsync(profile));
        ApplyProfileCommand = new Command<SavedDisplayProfile>(async (profile) => await ApplyProfileAsync(profile));
        OverwriteProfileCommand = new Command<SavedDisplayProfile>(async (profile) => await OverwriteProfileAsync(profile));
        EditLayoutCommand = new Command<SavedDisplayProfile>(OpenLayoutEditor);
        SetHotkeyCommand = new Command<SavedDisplayProfile>(StartHotkeyCapture);
        ClearHotkeyCommand = new Command<SavedDisplayProfile>(async (profile) => await ClearHotkeyAsync(profile));
        RefreshCommand = new Command(async () => await RefreshAllAsync());

#if WINDOWS
        InitializeWindowsServices();
#endif

        _ = RefreshAllAsync();
    }

#if WINDOWS
    void InitializeWindowsServices()
    {
        // Try to get services from DI
        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services != null)
        {
            _keyCaptureService = services.GetService<KeyCaptureService>();
            _globalHotkeyService = services.GetService<GlobalHotkeyService>();
            _trayIconService = services.GetService<TrayIconService>();

            if (_keyCaptureService != null)
            {
                _keyCaptureService.HotkeyCaptured += OnHotkeyCaptured;
                _keyCaptureService.CaptureCancelled += OnCaptureCancelled;
            }

            _startWithWindows = StartupService.IsStartupEnabled;
            OnPropertyChanged(nameof(StartWithWindows));
        }
    }

    void OnHotkeyCaptured(object? sender, HotkeyDefinition hotkey)
    {
        if (_hotkeyCapturingProfile == null)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _hotkeyCapturingProfile.Hotkey = hotkey;
            await _profileService.SaveProfileAsync(_hotkeyCapturingProfile);

            var capturedProfile = _hotkeyCapturingProfile;
            _hotkeyCapturingProfile = null;
            OnPropertyChanged(nameof(IsCapturingHotkey));
            OnPropertyChanged(nameof(CapturingProfileId));

            // Refresh the list to show updated hotkey
            await LoadProfilesAsync();

            // Re-register hotkeys
            RefreshGlobalHotkeys();

            await ShowSuccessAsync($"Hotkey set for '{capturedProfile.Name}'");
        });
    }

    void OnCaptureCancelled(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _hotkeyCapturingProfile = null;
            OnPropertyChanged(nameof(IsCapturingHotkey));
            OnPropertyChanged(nameof(CapturingProfileId));
        });
    }

    void RefreshGlobalHotkeys()
    {
        if (_globalHotkeyService != null)
        {
            _globalHotkeyService.RefreshFromProfiles(SavedProfiles.ToList());
        }
        if (_trayIconService != null)
        {
            _trayIconService.UpdateProfiles(SavedProfiles.ToList());
        }
    }
#endif

    void OpenLayoutEditor(SavedDisplayProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        var viewModel = new LayoutEditorViewModel(profile);
        var page = new MegaSchoen.LayoutEditorPage(viewModel);
        var window = new Window(page)
        {
            Title = $"Edit Layout — {profile.Name}",
            Width = 1100,
            Height = 880
        };
        Application.Current?.OpenWindow(window);
    }

    void StartHotkeyCapture(SavedDisplayProfile? profile)
    {
        if (profile == null)
        {
            return;
        }

#if WINDOWS
        if (_keyCaptureService == null)
        {
            _ = ShowErrorAsync("Hotkey capture is not available.");
            return;
        }

        _hotkeyCapturingProfile = profile;
        OnPropertyChanged(nameof(IsCapturingHotkey));
        OnPropertyChanged(nameof(CapturingProfileId));
        _keyCaptureService.StartCapture();
#else
        _ = ShowErrorAsync("Hotkey capture is only available on Windows.");
#endif
    }

    async Task ClearHotkeyAsync(SavedDisplayProfile? profile)
    {
        if (profile == null)
        {
            return;
        }

        profile.Hotkey = null;
        await _profileService.SaveProfileAsync(profile);
        await LoadProfilesAsync();

#if WINDOWS
        RefreshGlobalHotkeys();
#endif

        await ShowSuccessAsync($"Hotkey cleared for '{profile.Name}'");
    }

    static async Task ShowErrorAsync(string message)
    {
        var page = Application.Current?.Windows is { Count: > 0 } windows ? windows[0].Page : null;
        if (page != null)
        {
            await page.DisplayAlertAsync("Error", message, "OK");
        }
    }

    static async Task ShowSuccessAsync(string message)
    {
        var page = Application.Current?.Windows is { Count: > 0 } windows ? windows[0].Page : null;
        if (page != null)
        {
            await page.DisplayAlertAsync("Success", message, "OK");
        }
    }

    static async Task<bool> ConfirmAsync(string title, string message)
    {
        var page = Application.Current?.Windows is { Count: > 0 } windows ? windows[0].Page : null;
        if (page != null)
        {
            return await page.DisplayAlertAsync(title, message, "Yes", "No");
        }
        return false;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
