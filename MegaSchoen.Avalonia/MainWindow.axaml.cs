using System;
using System.Threading.Tasks;
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
            new NullSshSessionWindowResolver(),
            text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask);
        DataContext = viewModel;

        Opened += (_, _) => viewModel.Start();
        Closed += (_, _) => viewModel.Dispose();
    }
}
