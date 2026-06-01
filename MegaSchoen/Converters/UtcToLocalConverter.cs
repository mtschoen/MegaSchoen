using System.Globalization;

namespace MegaSchoen.Converters;

public class UtcToLocalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Utc)
            {
                return dateTime.ToLocalTime();
            }
            return dateTime;
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // One-way converter: bindings using UtcToLocalConverter are OneWay, so ConvertBack
        // is never invoked. NotSupported is the correct contract, not unfinished work.
        throw new NotSupportedException($"{nameof(UtcToLocalConverter)} is a one-way converter.");
    }
}
