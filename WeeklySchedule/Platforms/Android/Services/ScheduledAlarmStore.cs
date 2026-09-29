using System.Text.Json;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Platforms.Android.Services;

public static class ScheduledAlarmStore
{
    private const string ItemsKey = "scheduled_alarms";

    private static IPreferencesService? _preferences;

    public static void Initialize(IPreferencesService preferences)
    {
        _preferences = preferences;
    }

    public static List<ScheduledAlarm> Load()
    {
        try
        {
            var json = _preferences?.GetString(ItemsKey);
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
            _preferences?.SetString(ItemsKey, JsonSerializer.Serialize(alarms));
        }
        catch { }
    }
}