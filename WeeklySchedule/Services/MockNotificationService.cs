using System.Diagnostics;

namespace WeeklySchedule.Services;

public class MockNotificationService : INotificationService
{
    public Task<bool> CheckPermissionAsync() => Task.FromResult(true);
    public Task RequestPermissionAsync() => Task.CompletedTask;

    public Task<bool> CheckAllPermissionsAsync() => Task.FromResult(true);
    public Task RequestAllPermissionsAsync() => Task.CompletedTask;

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
