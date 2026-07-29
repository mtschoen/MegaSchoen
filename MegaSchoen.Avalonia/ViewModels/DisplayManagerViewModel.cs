using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Claude.Core;
using DisplayManager.Core;
using MegaSchoen.Avalonia.Services;

namespace MegaSchoen.Avalonia.ViewModels;

public sealed partial class DisplayManagerViewModel : INotifyPropertyChanged
{
    readonly IDisplayManagerService service;
    readonly Func<Task> refreshRuntimeHotkeys;
    IReadOnlyList<DisplayInfo> allDisplays = Array.Empty<DisplayInfo>();
    bool hideInactiveDisplays = true;
    bool isBusy;
    bool isError;
    string newProfileName = "";
    string statusMessage = "";

    internal DisplayManagerViewModel(IDisplayManagerService service)
        : this(service, () => Task.CompletedTask)
    {
    }

    internal DisplayManagerViewModel(
        IDisplayManagerService service,
        Func<Task> refreshRuntimeHotkeys)
    {
        this.service = service;
        this.refreshRuntimeHotkeys = refreshRuntimeHotkeys;

        RefreshCommand = new RelayCommand(_ => RunCommand(InitializeAsync));
        SaveCurrentArrangementCommand = new RelayCommand(_ => RunCommand(SaveCurrentArrangementAsync));
        ApplyProfileCommand = new RelayCommand(parameter =>
            RunCardCommand(parameter, ApplyProfileAsync));
        RequestDeleteCommand = new RelayCommand(parameter =>
        {
            if (parameter is DisplayProfileCardViewModel card)
            {
                RequestDelete(card);
            }
        });
        ConfirmDeleteCommand = new RelayCommand(parameter =>
            RunCardCommand(parameter, ConfirmDeleteAsync));
        RequestOverwriteCommand = new RelayCommand(parameter =>
        {
            if (parameter is DisplayProfileCardViewModel card)
            {
                RequestOverwrite(card);
            }
        });
        ConfirmOverwriteCommand = new RelayCommand(parameter =>
            RunCardCommand(parameter, ConfirmOverwriteAsync));
        CancelPendingCommand = new RelayCommand(parameter =>
        {
            if (parameter is DisplayProfileCardViewModel card)
            {
                CancelPending(card);
            }
        });
        BeginHotkeyCaptureCommand = new RelayCommand(parameter =>
        {
            if (parameter is DisplayProfileCardViewModel card)
            {
                BeginHotkeyCapture(card);
            }
        });
        ClearHotkeyCommand = new RelayCommand(parameter =>
            RunCardCommand(parameter, ClearHotkeyAsync));
    }

    public ObservableCollection<DisplayCardViewModel> CurrentDisplays { get; } = new();
    public ObservableCollection<DisplayProfileCardViewModel> SavedProfiles { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand SaveCurrentArrangementCommand { get; }
    public ICommand ApplyProfileCommand { get; }
    public ICommand RequestDeleteCommand { get; }
    public ICommand ConfirmDeleteCommand { get; }
    public ICommand RequestOverwriteCommand { get; }
    public ICommand ConfirmOverwriteCommand { get; }
    public ICommand CancelPendingCommand { get; }
    public ICommand BeginHotkeyCaptureCommand { get; }
    public ICommand ClearHotkeyCommand { get; }

    public string NewProfileName
    {
        get => newProfileName;
        set
        {
            if (newProfileName == value)
            {
                return;
            }

            newProfileName = value;
            OnPropertyChanged();
        }
    }

    public bool HideInactiveDisplays
    {
        get => hideInactiveDisplays;
        set
        {
            if (hideInactiveDisplays == value)
            {
                return;
            }

            hideInactiveDisplays = value;
            OnPropertyChanged();
            RebuildDisplays();
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (isBusy == value)
            {
                return;
            }

            isBusy = value;
            OnPropertyChanged();
        }
    }

    public bool IsError
    {
        get => isError;
        private set
        {
            if (isError == value)
            {
                return;
            }

            isError = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (statusMessage == value)
            {
                return;
            }

            statusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public bool HasNoDisplays => CurrentDisplays.Count == 0;
    public bool HasNoProfiles => SavedProfiles.Count == 0;

    async Task ReloadProfilesAsync()
    {
        var profiles = await service.GetProfilesAsync();
        SavedProfiles.Clear();
        foreach (var profile in profiles.OrderByDescending(profile => profile.LastModified))
        {
            SavedProfiles.Add(new DisplayProfileCardViewModel(profile));
        }

        OnPropertyChanged(nameof(HasNoProfiles));
    }

    void RebuildDisplays()
    {
        var displays = HideInactiveDisplays
            ? allDisplays.Where(display => display.IsActive)
            : allDisplays;

        CurrentDisplays.Clear();
        foreach (var display in displays)
        {
            CurrentDisplays.Add(new DisplayCardViewModel(display));
        }

        OnPropertyChanged(nameof(HasNoDisplays));
    }

    void CancelAllPending()
    {
        foreach (var profile in SavedProfiles)
        {
            CancelPending(profile);
        }
    }

    void SetSuccess(string message)
    {
        IsError = false;
        StatusMessage = message;
    }

    void SetError(string message)
    {
        IsError = true;
        StatusMessage = message;
    }

    void ClearStatus()
    {
        IsError = false;
        StatusMessage = "";
    }

    static void RunCardCommand(
        object? parameter,
        Func<DisplayProfileCardViewModel, Task> operation)
    {
        if (parameter is DisplayProfileCardViewModel card)
        {
            RunCommand(() => operation(card));
        }
    }

    static void RunCommand(Func<Task> operation) => _ = RunAndLogAsync(operation);

    static async Task RunAndLogAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            Logger.Log($"DisplayManagerViewModel command failed: {exception}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
