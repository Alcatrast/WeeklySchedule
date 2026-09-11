using System.Text.Json;
using global::Android.Content;
using Application = global::Android.App.Application;
using WeeklySchedule.Models;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Platforms.Android.Services;

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

public static class ScheduledAlarmStore
{
    private const string PreferencesName = "weekly_schedule_alarms";
    private const string ItemsKey = "scheduled";

    private static ISharedPreferences? Preferences =>
        Application.Context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);

    public static List<ScheduledAlarm> Load()
    {
        try
        {
            var json = Preferences?.GetString(ItemsKey, null);
            if (string.IsNullOrEmpty(json)) return [];
            return JsonSerializer.Deserialize<List<ScheduledAlarm>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(List<ScheduledAlarm> alarms)
    {
        try
        {
            var editor = Preferences?.Edit();
            if (editor == null) return;
            editor.PutString(ItemsKey, JsonSerializer.Serialize(alarms));
            editor.Apply();
        }
        catch { }
    }
}
