using System.Globalization;
using WeeklySchedule.Models;

namespace WeeklySchedule.Converters;

public class SeparatorTypeToHeightConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SeparatorType type)
        {
            return type switch
            {
                SeparatorType.ThickWhite => 4.0,
                _ => 0.0
            };
        }
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}