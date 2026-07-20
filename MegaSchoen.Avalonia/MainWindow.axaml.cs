using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Claude.Core;
using MegaSchoen.Avalonia.ViewModels;

namespace MegaSchoen.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Held via the event closures rather than a field: the window is not
        // IDisposable, so the viewmodel's lifetime is tied to Opened/Closed.
        var viewModel = new SessionsViewModel(
            SessionServices.BuildEnumerator(),
            OperatingSystem.IsLinux()
                ? new Claude.Core.Linux.LinuxClaudeWindowFocuser()
                : new NullClaudeWindowFocuser(),
            OperatingSystem.IsLinux()
                ? new Claude.Core.Linux.LinuxSshSessionWindowResolver()
                : new NullSshSessionWindowResolver(),
            text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask);
        DataContext = viewModel;

        AutostartToggle.IsChecked = AutostartService.IsEnabled;
        AutostartToggle.IsCheckedChanged += (_, _) =>
        {
            try
            {
                AutostartService.SetEnabled(AutostartToggle.IsChecked == true);
            }
            catch (Exception exception)
            {
                Logger.Log($"MainWindow: autostart toggle failed: {exception}");
                AutostartToggle.IsChecked = AutostartService.IsEnabled;
            }
        };

        Opened += (_, _) => viewModel.Start();
        Closed += (_, _) => viewModel.Dispose();

        // Hide to tray on close; the watchers keep running so the monitor
        // stays live in the background. The tray's Exit sets ExitRequested
        // and shuts down for real (Closed then disposes the viewmodel).
        Closing += (_, eventArguments) =>
        {
            if (Application.Current is App { ExitRequested: false })
            {
                eventArguments.Cancel = true;
                Hide();
            }
        };
    }
}
