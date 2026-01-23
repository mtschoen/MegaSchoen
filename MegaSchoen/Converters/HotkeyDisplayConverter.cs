using System.Globalization;
using DisplayManager.Core.Models;

namespace MegaSchoen.Converters;

/// <summary>
/// Converts a HotkeyDefinition to a display string like "Ctrl+Alt+1".
/// </summary>
class HotkeyDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HotkeyDefinition hotkey || !hotkey.Enabled || string.IsNullOrEmpty(hotkey.Key))
        {
            return "Set Hotkey";
        }

        var parts = new List<string>();
        foreach (var mod in hotkey.Modifiers)
        {
            parts.Add(mod switch
            {
                "Control" => "Ctrl",
                _ => mod
            });
        }
        parts.Add(hotkey.Key);
        return string.Join("+", parts);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
