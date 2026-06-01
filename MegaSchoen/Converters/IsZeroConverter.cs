using System.Globalization;

namespace MegaSchoen.Converters;

public class IsZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue == 0;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // One-way converter: bindings using IsZeroConverter are OneWay, so ConvertBack
        // is never invoked. NotSupported is the correct contract, not unfinished work.
        throw new NotSupportedException($"{nameof(IsZeroConverter)} is a one-way converter.");
    }
}
