using WeeklySchedule.Services;
using Application = global::Android.App.Application;

namespace WeeklySchedule.Platforms.Android.Services;

public class AndroidPreferencesService : IPreferencesService
{
    private const string PreferencesName = "weekly_schedule_prefs";

    private static global::Android.Content.ISharedPreferences? Preferences =>
        Application.Context.GetSharedPreferences(PreferencesName,
            global::Android.Content.FileCreationMode.Private);

    public string? GetString(string key, string? defaultValue = null)
        => Preferences?.GetString(key, defaultValue);

    public void SetString(string key, string value)
    {
        using var editor = Preferences?.Edit();
        editor?.PutString(key, value);
        editor?.Apply();
    }

    public bool GetBool(string key, bool defaultValue = false)
        => Preferences?.GetBoolean(key, defaultValue) ?? defaultValue;

    public void SetBool(string key, bool value)
    {
        using var editor = Preferences?.Edit();
        editor?.PutBoolean(key, value);
        editor?.Apply();
    }

    public int GetInt(string key, int defaultValue = 0)
        => Preferences?.GetInt(key, defaultValue) ?? defaultValue;

    public void SetInt(string key, int value)
    {
        using var editor = Preferences?.Edit();
        editor?.PutInt(key, value);
        editor?.Apply();
    }
}