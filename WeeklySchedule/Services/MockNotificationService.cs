using System.Diagnostics;
using WeeklySchedule.Models;

namespace WeeklySchedule.Services;

public class MockNotificationService : INotificationService
{
    public Task<bool> CheckPermissionAsync() => Task.FromResult(true);
    public Task RequestPermissionAsync() => Task.CompletedTask;

    public Task<bool> CheckAllPermissionsAsync() => Task.FromResult(true);
    public Task RequestAllPermissionsAsync() => Task.CompletedTask;

    public void ScheduleNotification(Guid timelineId, Guid lessonId, string title, string body, DateTime triggerTime, int minutesBefore)
    {
#if DEBUG
        Debug.WriteLine($"[MOCK Notification] Запланировано: '{title}' на {triggerTime}");
#endif
    }
    public void CancelNotificationsForLesson(Guid lessonId) { }
    public void CancelAllNotifications() { }
}