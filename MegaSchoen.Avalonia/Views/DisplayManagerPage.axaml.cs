using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MegaSchoen.Avalonia.Services;
using MegaSchoen.Avalonia.ViewModels;

namespace MegaSchoen.Avalonia.Views;

public partial class DisplayManagerPage : UserControl
{
    bool initialized;

    public DisplayManagerPage()
    {
        InitializeComponent();
        IsVisible = OperatingSystem.IsWindows();
        if (!IsVisible)
        {
            return;
        }

        DataContext = new DisplayManagerViewModel(
            new DisplayManagerService(),
            App.RefreshDisplayHotkeysAsync);
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArguments)
    {
        if (initialized || DataContext is not DisplayManagerViewModel viewModel)
        {
            return;
        }

        initialized = true;
        await viewModel.InitializeAsync();
    }

    async void OnHotkeyKeyDown(object? sender, KeyEventArgs eventArguments)
    {
        if (sender is not Button { DataContext: DisplayProfileCardViewModel card } ||
            DataContext is not DisplayManagerViewModel viewModel)
        {
            return;
        }

        if (eventArguments.Key == Key.Escape)
        {
            viewModel.CancelPending(card);
            eventArguments.Handled = true;
            return;
        }

        if (IsModifier(eventArguments.Key))
        {
            return;
        }

        var modifiers = GetModifiers(eventArguments.KeyModifiers);
        await viewModel.AssignHotkeyAsync(card, FormatKey(eventArguments.Key), modifiers);
        eventArguments.Handled = true;
    }

    static bool IsModifier(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin;

    static List<string> GetModifiers(KeyModifiers modifiers)
    {
        var result = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            result.Add("Control");
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            result.Add("Alt");
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            result.Add("Shift");
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            result.Add("Win");
        }

        return result;
    }

    static string FormatKey(Key key)
    {
        var text = key.ToString();
        return text is { Length: 2 } && text[0] == 'D' && char.IsDigit(text[1])
            ? text[1].ToString()
            : text;
    }
}
