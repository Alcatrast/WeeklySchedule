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
                SeparatorType.ThickWhite => GetColor("OutlineLight", "OutlineDark"),
                _ => Colors.Transparent
            };
        }
        return Colors.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static Color GetColor(string lightKey, string darkKey)
    {
        bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        string key = isDark ? darkKey : lightKey;

        if (Application.Current?.Resources.TryGetValue(key, out var colorObj) == true && colorObj is Color color)
        {
            return color;
        }

        return Colors.Transparent;
    }
}