using System;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MegaSchoen.Avalonia;

sealed class WindowsAutostartBackend : IAutostartBackend
{
    readonly IStartupValueStore _store;
    readonly Func<string?> _executablePath;

    public WindowsAutostartBackend(IStartupValueStore store, Func<string?> executablePath)
    {
        _store = store;
        _executablePath = executablePath;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_store.Read());

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            _store.Write(null);
            return;
        }

        var executablePath = _executablePath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Could not determine the application executable path.");
        }

        _store.Write($"\"{executablePath}\" --hidden");
    }
}

interface IStartupValueStore
{
    string? Read();

    void Write(string? value);
}

[SupportedOSPlatform("windows")]
sealed class RegistryStartupValueStore : IStartupValueStore
{
    const string Subkey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "MegaSchoen.Avalonia";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Subkey);
        return key?.GetValue(ValueName) as string;
    }

    public void Write(string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Subkey);
        if (value is null)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        key.SetValue(ValueName, value, RegistryValueKind.String);
    }
}
