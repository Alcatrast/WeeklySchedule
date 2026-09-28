using System.Globalization;

namespace WeeklySchedule.Converters;

public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isSelected = value is true;

        if (isSelected)
        {
            return GetColor("AccentLight", "AccentDark");
        }

        return GetColor("SurfaceVariantLight", "SurfaceVariantDark");
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

        return Colors.Gray;
    }
}