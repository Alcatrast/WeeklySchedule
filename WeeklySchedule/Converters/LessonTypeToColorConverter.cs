using System.Globalization;
using WeeklySchedule.Models;

namespace WeeklySchedule.Converters;

public class LessonTypeToColorConverter : IValueConverter
{
    private static readonly Dictionary<LessonType, (string LightKey, string DarkKey)> ColorMap = new()
    {
        [LessonType.Lecture] = ("LessonLectureLight", "LessonLectureDark"),
        [LessonType.Seminar] = ("LessonSeminarLight", "LessonSeminarDark"),
        [LessonType.Practice] = ("LessonPracticeLight", "LessonPracticeDark"),
        [LessonType.Lab] = ("LessonLabLight", "LessonLabDark"),
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LessonType type && ColorMap.TryGetValue(type, out var keys))
        {
            return GetColor(keys.LightKey, keys.DarkKey);
        }
        return Colors.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();

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