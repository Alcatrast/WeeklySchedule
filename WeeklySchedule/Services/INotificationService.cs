namespace WeeklySchedule.Services;

public interface INotificationService
{
    Task<bool> CheckPermissionAsync();
    Task RequestPermissionAsync();

    // Новые методы для комплексной проверки всех 3 разрешений
    Task<bool> CheckAllPermissionsAsync();
    Task RequestAllPermissionsAsync();

    void ScheduleNotification(Guid timelineId, Guid lessonId, string title, string body, DateTime triggerTime, int minutesBefore);
    void CancelNotificationsForLesson(Guid lessonId);
    void CancelAllNotifications();
}