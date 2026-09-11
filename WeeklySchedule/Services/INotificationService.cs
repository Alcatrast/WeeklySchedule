namespace WeeklySchedule.Services;

public interface INotificationService
{
    Task<bool> CheckPermissionAsync();
    Task RequestPermissionAsync();
    Task<bool> CheckAllPermissionsAsync();
    Task RequestAllPermissionsAsync();

    void ScheduleNotification(Guid timelineId, Guid lessonId, string title, string body, DateTime triggerTime, int minutesBefore);
    void CancelNotificationsForLesson(Guid lessonId);
    void CancelAllNotifications();
}