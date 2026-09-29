namespace WeeklySchedule.Services;

public interface IPreferencesService
{
    string? GetString(string key, string? defaultValue = null);
    void SetString(string key, string value);
    bool GetBool(string key, bool defaultValue = false);
    void SetBool(string key, bool value);
    int GetInt(string key, int defaultValue = 0);
    void SetInt(string key, int value);
}