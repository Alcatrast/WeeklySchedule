using System.Text.Json;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Models;

public sealed class ScheduledAlarm
{
    public int NotificationId { get; set; }
    public string TimelineId { get; set; } = string.Empty;
    public string LessonId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public long TriggerAtMillis { get; set; }
    public int MinutesBefore { get; set; }
    public DayOfWeek? LessonDay { get; set; }
    public TimeSpan? LessonStartTime { get; set; }

    public bool MoveToNextOccurrence(DateTimeOffset after, TimeZoneInfo zone)
    {
        if (LessonDay == null || LessonStartTime == null)
        {
            if (!Guid.TryParse(TimelineId, out var timelineId) || !Guid.TryParse(LessonId, out var lessonId)) return false;
            var path = Path.Combine(FileSystem.AppDataDirectory, "Timelines", timelineId.ToString(), "Lessons", $"{lessonId}.json");
            if (!File.Exists(path)) return false;

            var lesson = JsonSerializer.Deserialize<Lesson>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (lesson == null) return false;

            LessonDay = lesson.Day;
            LessonStartTime = lesson.StartTime;
        }

        TriggerAtMillis = WeeklyOccurrence.Next(LessonDay.Value, LessonStartTime.Value,
            MinutesBefore, after, zone).ToUnixTimeMilliseconds();
        return true;
    }
}