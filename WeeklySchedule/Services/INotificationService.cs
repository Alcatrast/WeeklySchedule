namespace WeeklySchedule.Services;

public interface INotificationService
{
    Task<bool> CheckPermissionAsync();
    Task RequestPermissionAsync();

    // Комплексная проверка всех 3 разрешений
    Task<bool> CheckAllPermissionsAsync();
    Task RequestAllPermissionsAsync();

    /// <summary>
    /// Ставит еженедельное напоминание за <paramref name="minutesBefore"/> минут до
    /// начала пары. Реализация сама находит ближайшее будущее вхождение: расписание
    /// недельное, поэтому дня недели и времени начала достаточно, а конкретная дата
    /// зависит от часового пояса и перевода часов и считается на стороне платформы.
    /// </summary>
    void ScheduleNotification(Guid timelineId, Guid lessonId, string title, string body,
        DayOfWeek day, TimeSpan startTime, int minutesBefore);

    void CancelAllNotifications();
}
