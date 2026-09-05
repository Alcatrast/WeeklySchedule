using WeeklySchedule.Models;

namespace WeeklySchedule.Services;

public interface ISettingsService
{
    AppTheme Theme { get; set; }
    int DefaultLessonDuration { get; set; }
    bool OpenLastTimeline { get; set; }
    Guid StartupTimelineId { get; set; }
    bool NotifyAtStart { get; set; }
    List<NotificationReminder> NotifyBeforeList { get; set; }
    event Action? SettingsChanged;
}