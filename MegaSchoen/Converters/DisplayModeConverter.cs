using System.Globalization;
using DisplayManager.Core.Models;

namespace MegaSchoen.Converters;

/// <summary>Formats a DisplayMode as "1920 x 1080 @ 60Hz" for the Advanced picker.</summary>
public class DisplayModeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DisplayMode m ? $"{m.Width} x {m.Height} @ {m.RefreshRate:F0}Hz" : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
