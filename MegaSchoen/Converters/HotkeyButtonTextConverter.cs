using System.Globalization;
using DisplayManager.Core.Models;

namespace MegaSchoen.Converters;

/// <summary>
/// Multi-value converter for hotkey button text.
/// Values: [0] = Hotkey (HotkeyDefinition?), [1] = Profile Id (Guid), [2] = CapturingProfileId (Guid?)
/// Returns "Press keys..." if this profile is being captured, otherwise the hotkey display text.
/// </summary>
class HotkeyButtonTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3)
        {
            return "Set Hotkey";
        }

        var hotkey = values[0] as HotkeyDefinition;
        var profileId = values[1] is Guid id ? id : Guid.Empty;
        var capturingId = values[2] as Guid?;

        // Check if this profile is being captured
        if (capturingId.HasValue && capturingId.Value == profileId)
        {
            return "Press keys...";
        }

        // Otherwise show the hotkey or "Set Hotkey"
        if (hotkey == null || !hotkey.Enabled || string.IsNullOrEmpty(hotkey.Key))
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

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
