using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Platforms.Android.Services;
using WeeklySchedule.Resources.Strings;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Services;

public class NotificationSchedulerService(
        ITimelineRepository timelineRepository,
        ILessonRepository lessonRepository,
        INotificationService notificationService) : INotificationSchedulerService
{
    private readonly ITimelineRepository _timelineRepository = timelineRepository;
    private readonly ILessonRepository _lessonRepository = lessonRepository;
    private readonly INotificationService _notificationService = notificationService;

    public async Task CancelAllAsync()
    {
        _notificationService.CancelAllNotifications();
    }

    public async Task ScheduleAllAsync(Guid timelineId, bool notifyAtStart,
        List<NotificationReminder> reminders, DateTime now)
    {
        var timeline = await _timelineRepository.GetByIdAsync(timelineId);
        var lessons = (await _lessonRepository.GetByTimelineIdAsync(timelineId)).ToList();

        if (timeline == null || !timeline.NotificationsEnabled) return;

        var activeReminders = reminders
            .Where(r => r.IsActive && r.MinutesBefore >= 0 && r.MinutesBefore <= 7 * 24 * 60)
            .ToList();

        if (!notifyAtStart && activeReminders.Count == 0) return;

        foreach (var lesson in lessons)
        {
            if (notifyAtStart)
            {
                var start = GetNextOccurrence(lesson, now);
                _notificationService.ScheduleNotification(
                    timeline.Id, lesson.Id,
                    string.Format(AppResources.NotifyLessonStartMsg, lesson.Name),
                    lesson.Description, start, 0);
            }

            foreach (var reminder in activeReminders)
            {
                var triggerTime = GetNextOccurrence(lesson, now.AddMinutes(reminder.MinutesBefore));
                _notificationService.ScheduleNotification(
                    timeline.Id, lesson.Id,
                    string.Format(AppResources.NotifyLessonSoonTitle, lesson.Name),
                    string.Format(AppResources.NotifyLessonSoonMsg, reminder.MinutesBefore, lesson.Description),
                    triggerTime, reminder.MinutesBefore);
            }
        }
    }

    private static DateTime GetNextOccurrence(Lesson lesson, DateTime from)
    {
        int daysUntil = ((int)lesson.Day - (int)from.DayOfWeek + 7) % 7;
        var occurrence = from.Date.AddDays(daysUntil).Add(lesson.StartTime);
        return occurrence > from ? occurrence : occurrence.AddDays(7);
    }
}