namespace WeeklySchedule.Utilities;

public static class WeeklyOccurrence
{
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
            while (zone.IsInvalidTime(localStart)) localStart = localStart.AddMinutes(1);
            var trigger = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone))
                .AddMinutes(-minutesBefore);
            if (trigger > after) return trigger;
            date = date.AddDays(7);
        }
    }
}
