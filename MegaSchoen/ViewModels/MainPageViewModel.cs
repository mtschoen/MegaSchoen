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

public class MainPageViewModel : INotifyPropertyChanged
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
    StartupService? _startupService;
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
                _startupService?.SetStartupEnabled(value);
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
    public ICommand SetHotkeyCommand { get; }
    public ICommand ClearHotkeyCommand { get; }

    public MainPageViewModel()
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
        SetHotkeyCommand = new Command<SavedDisplayProfile>(StartHotkeyCapture);
        ClearHotkeyCommand = new Command<SavedDisplayProfile>(async (profile) => await ClearHotkeyAsync(profile));
        RefreshCommand = new Command(async () => await RefreshAllAsync());

#if WINDOWS
        InitializeWindowsServices();
#endif

        // Load initial data
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
            _startupService = services.GetService<StartupService>();

            if (_keyCaptureService != null)
            {
                _keyCaptureService.HotkeyCaptured += OnHotkeyCaptured;
                _keyCaptureService.CaptureCancelled += OnCaptureCancelled;
            }

            // Initialize startup state from actual shortcut
            if (_startupService != null)
            {
                _startWithWindows = _startupService.IsStartupEnabled;
                OnPropertyChanged(nameof(StartWithWindows));
            }
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

            var profile = _profileService.CaptureCurrentConfiguration(
                NewProfileName.Trim(),
                $"Captured on {DateTime.Now:g}"
            );

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

    async Task ShowErrorAsync(string message)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page != null)
        {
            await page.DisplayAlertAsync("Error", message, "OK");
        }
    }

    async Task ShowSuccessAsync(string message)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page != null)
        {
            await page.DisplayAlertAsync("Success", message, "OK");
        }
    }

    async Task<bool> ConfirmAsync(string title, string message)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
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
