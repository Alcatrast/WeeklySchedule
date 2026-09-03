using System.Globalization;

namespace WeeklySchedule.Converters;

public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isSelected = value is true;
        bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

        if (isSelected)
        {
            // Выделенная кнопка: используем Primary цвет из Colors.xaml
            return isDark ? Color.FromArgb("#ac99ea") : Color.FromArgb("#512BD4");
        }
        else
        {
            // Невыделенная кнопка: светлая тема - светло-серый, темная - темно-серый
            return isDark ? Color.FromArgb("#404040") : Color.FromArgb("#E1E1E1");
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}