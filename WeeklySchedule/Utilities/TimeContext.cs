namespace WeeklySchedule.Utilities;

public static class TimeContext
{
    private static TimeSpan _debugOffset = TimeSpan.Zero;
    public static DateTime Now
    {
        get => DateTime.Now + _debugOffset;
        set => _debugOffset = value - DateTime.Now;
    }
}