using WeeklySchedule.Models;

namespace WeeklySchedule.Services;

public interface INotificationSchedulerService
{
    Task ScheduleAllAsync(Guid timelineId, bool notifyAtStart,
        List<NotificationReminder> reminders, DateTime now);
    Task CancelAllAsync();
}