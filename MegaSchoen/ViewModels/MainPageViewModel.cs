using DisplayManager.Core;
using DisplayManager.Core.Models;
using DisplayManager.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MegaSchoen.ViewModels
{
    public class MainPageViewModel : INotifyPropertyChanged
    {
        private readonly DisplayProfileService _profileService;
        private bool _isLoading;
        private string _newProfileName = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<DisplayManager.Core.DisplayInfo> CurrentDisplays { get; } = new();
        public ObservableCollection<SavedDisplayProfile> SavedProfiles { get; } = new();

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

        public ICommand LoadDisplaysCommand { get; }
        public ICommand SaveCurrentArrangementCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ApplyProfileCommand { get; }

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
            RefreshCommand = new Command(async () => await RefreshAllAsync());

            // Load initial data
            _ = RefreshAllAsync();
        }

        private async Task LoadDisplaysAsync()
        {
            try
            {
                var displays = DisplayManager.Core.DisplayManager.GetAllDisplays();

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

        private async Task LoadProfilesAsync()
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

        private async Task SaveCurrentArrangementAsync()
        {
            if (string.IsNullOrWhiteSpace(NewProfileName))
                return;

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

        private async Task DeleteProfileAsync(SavedDisplayProfile? profile)
        {
            if (profile == null)
                return;

            try
            {
                bool confirmed = await ConfirmAsync(
                    $"Delete Profile",
                    $"Are you sure you want to delete '{profile.Name}'?"
                );

                if (!confirmed)
                    return;

                IsLoading = true;

                await _profileService.DeleteProfileAsync(profile.Id);
                await LoadProfilesAsync();

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

        private async Task ApplyProfileAsync(SavedDisplayProfile? profile)
        {
            if (profile == null)
                return;

            try
            {
                IsLoading = true;

                bool success = _profileService.ApplyProfile(profile);

                if (success)
                {
                    await ShowSuccessAsync($"Profile '{profile.Name}' applied successfully!");

                    // Refresh displays to show the new configuration
                    await Task.Delay(500); // Small delay to let Windows settle
                    await LoadDisplaysAsync();
                }
                else
                {
                    await ShowErrorAsync($"Failed to apply profile '{profile.Name}'. Check the system logs for details.");
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

        private async Task RefreshAllAsync()
        {
            IsLoading = true;
            try
            {
                await LoadDisplaysAsync();
                await LoadProfilesAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ShowErrorAsync(string message)
        {
            if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.DisplayAlert("Error", message, "OK");
            }
        }

        private async Task ShowSuccessAsync(string message)
        {
            if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.DisplayAlert("Success", message, "OK");
            }
        }

        private async Task<bool> ConfirmAsync(string title, string message)
        {
            if (Application.Current?.MainPage != null)
            {
                return await Application.Current.MainPage.DisplayAlert(title, message, "Yes", "No");
            }
            return false;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
