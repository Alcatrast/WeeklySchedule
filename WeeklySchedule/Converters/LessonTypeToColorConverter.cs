using System.Globalization;
using WeeklySchedule.Models;

namespace WeeklySchedule.Converters;

public class LessonTypeToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LessonType type)
        {
            bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

            return type switch
            {
                LessonType.Lecture => isDark ? Color.FromArgb("#1E3A8A") : Color.FromArgb("#DBEAFE"), // Темно-синий / Светло-синий
                LessonType.Seminar => isDark ? Color.FromArgb("#065F46") : Color.FromArgb("#D1FAE5"), // Темно-зеленый / Светло-зеленый
                LessonType.Practice => isDark ? Color.FromArgb("#92400E") : Color.FromArgb("#FEF3C7"), // Темно-оранжевый / Светло-желтый
                LessonType.Lab => isDark ? Color.FromArgb("#7F1D1D") : Color.FromArgb("#FEE2E2"),     // Темно-красный / Светло-красный
                _ => Colors.Gray
            };
        }
        return Colors.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}