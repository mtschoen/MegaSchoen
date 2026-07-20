using System;
using System.Windows.Input;

namespace MegaSchoen.Avalonia.ViewModels;

// Minimal ICommand for this app's always-executable commands (no MAUI Command
// here, and no need to pull in a full MVVM toolkit for four commands).
sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter);
}
