using System.Text.Json;
using global::Android.Content;
using Application = global::Android.App.Application;
using WeeklySchedule.Models;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Platforms.Android.Services;

/// <summary>
/// Один поставленный в AlarmManager будильник.
/// </summary>
public sealed class ScheduledAlarm
{
    public int NotificationId { get; set; }
    public string TimelineId { get; set; } = string.Empty;
    public string LessonId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>Момент срабатывания в Unix-миллисекундах (уже с учетом MinutesBefore).</summary>
    public long TriggerAtMillis { get; set; }

    public int MinutesBefore { get; set; }
    public DayOfWeek? LessonDay { get; set; }
    public TimeSpan? LessonStartTime { get; set; }

    public bool MoveToNextOccurrence(DateTimeOffset after, TimeZoneInfo zone)
    {
        // Миграция будильников 1.0.5: в них сохранен только Unix timestamp.
        // Настоящие день и время берем из пары, а не из уже сменившегося часового пояса.
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

/// <summary>
/// Список поставленных будильников в SharedPreferences.
///
/// Раньше он жил только в памяти сервиса, поэтому после перезапуска процесса
/// CancelAllNotifications не отменял ничего: старые будильники оставались в
/// системе, а приложение ставило поверх них новые, и уведомления двоились.
/// Тот же список читает BootReceiver, чтобы восстановить будильники после
/// перезагрузки телефона и после установки нового apk.
/// </summary>
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
            // Битый список — не повод падать: хуже, чем потерянные будильники,
            // только приложение, которое не запускается
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
