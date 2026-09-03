using System.Globalization;

namespace WeeklySchedule.Converters;

public class IsEqualConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is Guid id1 && values[1] is Guid id2)
        {
            return id1 == id2 && id1 != Guid.Empty;
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}