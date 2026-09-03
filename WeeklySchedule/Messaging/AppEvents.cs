namespace WeeklySchedule.Messaging;

public static class AppEvents
{
    public static event Action<DayOfWeek?>? DataChanged;

    public static void NotifyDataChanged(DayOfWeek? affectedDay = null)
    {
        DataChanged?.Invoke(affectedDay);
    }
}