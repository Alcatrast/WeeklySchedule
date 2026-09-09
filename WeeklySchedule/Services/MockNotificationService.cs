using System.Diagnostics;

namespace WeeklySchedule.Services;

public class MockNotificationService : INotificationService
{
    public Task<bool> CheckPermissionAsync() => Task.FromResult(true);
    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
    public Task<bool> OpenNotificationSettingsAsync() => Task.FromResult(true);

    public Task<bool> CanScheduleExactAlarmsAsync() => Task.FromResult(true);
    public Task<bool> RequestExactAlarmsAsync() => Task.FromResult(true);

    public void ScheduleNotification(Guid timelineId, Guid lessonId, string title, string body,
        DayOfWeek day, TimeSpan startTime, int minutesBefore)
    {
#if DEBUG
        Debug.WriteLine($"[MOCK Notification] Запланировано: '{title}' на {day} {startTime:hh\\:mm} " +
            $"(за {minutesBefore} мин.)");
#endif
    }

    public void CancelAllNotifications() { }
}
