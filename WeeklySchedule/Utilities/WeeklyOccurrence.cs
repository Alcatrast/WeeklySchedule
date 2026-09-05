namespace WeeklySchedule.Utilities;

public static class WeeklyOccurrence
{
    // Сначала находим местное начало пары и только затем переводим его в UTC.
    // Напоминание отсчитывается реальными минутами до начала, в том числе на DST.
    public static DateTimeOffset Next(DayOfWeek day, TimeSpan startTime, int minutesBefore,
        DateTimeOffset after, TimeZoneInfo zone)
    {
        if (!Enum.IsDefined(day) || startTime < TimeSpan.Zero || startTime >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(startTime));
        if (minutesBefore < 0 || minutesBefore > 7 * 24 * 60)
            throw new ArgumentOutOfRangeException(nameof(minutesBefore));

        var localAfter = TimeZoneInfo.ConvertTime(after, zone).DateTime;
        var date = localAfter.Date.AddDays(((int)day - (int)localAfter.DayOfWeek + 7) % 7);
        while (true)
        {
            var localStart = DateTime.SpecifyKind(date.Add(startTime), DateTimeKind.Unspecified);
            // При переводе часов вперед отсутствующее время переносим на ближайшую
            // существующую минуту. При переводе назад выбирается стандартное время.
            while (zone.IsInvalidTime(localStart)) localStart = localStart.AddMinutes(1);
            var trigger = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone))
                .AddMinutes(-minutesBefore);
            if (trigger > after) return trigger;
            date = date.AddDays(7);
        }
    }
}
