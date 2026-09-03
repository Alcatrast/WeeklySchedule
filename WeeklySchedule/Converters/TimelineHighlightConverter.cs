using System.Globalization;

namespace WeeklySchedule.Converters;

public class TimelineHighlightConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // Всегда возвращаем нейтральный цвет фона, зеленая подсветка удалена
        bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        return isDark ? Color.FromArgb("#2C2C2C") : Color.FromArgb("#E1E1E1");
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class TimelineStrokeConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // Всегда возвращаем нейтральный цвет обводки
        bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        return isDark ? Color.FromArgb("#555555") : Color.FromArgb("#ACACAC");
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}