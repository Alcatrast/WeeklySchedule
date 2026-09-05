using System.Globalization;
using WeeklySchedule.Models;

namespace WeeklySchedule.Converters;

public class SeparatorTypeToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SeparatorType type)
        {
            return type switch
            {
                SeparatorType.ThickWhite => Colors.LightGray,
                _ => Colors.Transparent
            };
        }
        return Colors.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}