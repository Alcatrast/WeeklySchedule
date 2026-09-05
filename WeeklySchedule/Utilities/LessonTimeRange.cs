namespace WeeklySchedule.Utilities;

public static class LessonTimeRange
{
    public static readonly TimeSpan LatestEnd = new(23, 59, 0);

    public static TimeSpan NormalizeEnd(TimeSpan start, TimeSpan end) =>
        end <= start ? TimeSpan.FromTicks(Math.Min(start.Add(TimeSpan.FromMinutes(1)).Ticks, LatestEnd.Ticks))
        : TimeSpan.FromTicks(Math.Min(end.Ticks, LatestEnd.Ticks));

    public static bool IsValid(TimeSpan start, TimeSpan end) =>
        start >= TimeSpan.Zero && start < end && end <= LatestEnd;
}
