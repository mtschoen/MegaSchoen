using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Claude.Core;

namespace MegaSchoen.Avalonia.ViewModels;

// Minimal commands for this app; keep command state and asynchronous execution
// local instead of adding an MVVM framework dependency.
sealed class RelayCommand : ICommand
{
    readonly Action<object?> execute;
    readonly Func<object?, bool>? canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

sealed class AsyncRelayCommand : ICommand
{
    readonly Func<Task> execute;
    readonly Func<bool>? canExecute;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _ = ExecuteAndLogAsync();

    async Task ExecuteAndLogAsync()
    {
        try
        {
            await execute();
        }
        catch (Exception exception)
        {
            Logger.Log($"AsyncRelayCommand execution failed: {exception}");
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
