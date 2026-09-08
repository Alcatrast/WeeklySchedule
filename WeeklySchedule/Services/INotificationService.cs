namespace WeeklySchedule.Services;

public interface INotificationService
{
    /// <summary>
    /// Единственное разрешение, без которого уведомлений не будет вовсе.
    /// </summary>
    Task<bool> CheckPermissionAsync();
    Task RequestPermissionAsync();

    /// <summary>
    /// Может ли система будить приложение точно в срок. Это не право на доставку,
    /// а только ее точность: будильники ставятся флагом «разрешено в простое», и
    /// без точности они все равно срабатывают, но с задержкой в несколько минут.
    /// Отдельно от <see cref="CheckPermissionAsync"/> именно поэтому — отказ здесь
    /// не должен закрывать пользователю настройки уведомлений.
    /// </summary>
    Task<bool> CanScheduleExactAlarmsAsync();
    Task RequestExactAlarmsAsync();

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
