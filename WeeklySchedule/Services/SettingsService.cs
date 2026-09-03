using System.Text.Json;
using WeeklySchedule.Models;

namespace WeeklySchedule.Services;

public class SettingsService : ISettingsService
{
    private readonly string _startupTimelineFilePath;

    public event Action? SettingsChanged;

    public SettingsService()
    {
        _startupTimelineFilePath = Path.Combine(FileSystem.AppDataDirectory, "startup_timeline.guid");
    }

    public AppTheme Theme
    {
        get => (AppTheme)Preferences.Get(nameof(Theme), (int)AppTheme.Unspecified);
        set
        {
            Preferences.Set(nameof(Theme), (int)value);
            Application.Current?.UserAppTheme = value;
            SettingsChanged?.Invoke();
        }
    }

    public int DefaultLessonDuration
    {
        get => Preferences.Get(nameof(DefaultLessonDuration), 85);
        set
        {
            Preferences.Set(nameof(DefaultLessonDuration), value);
            SettingsChanged?.Invoke();
        }
    }

    public AppLanguage Language
    {
        get => (AppLanguage)Preferences.Get(nameof(Language), (int)AppLanguage.Russian);
        set
        {
            Preferences.Set(nameof(Language), (int)value);
            SettingsChanged?.Invoke();
        }
    }

    public bool OpenLastTimeline
    {
        get => Preferences.Get(nameof(OpenLastTimeline), true);
        set
        {
            Preferences.Set(nameof(OpenLastTimeline), value);
            SettingsChanged?.Invoke();
        }
    }

    public Guid StartupTimelineId
    {
        get
        {
            try
            {
                if (File.Exists(_startupTimelineFilePath))
                {
                    var str = File.ReadAllText(_startupTimelineFilePath).Trim();
                    return Guid.TryParse(str, out var id) ? id : Guid.Empty;
                }
            }
            catch { }
            return Guid.Empty;
        }
        set
        {
            try
            {
                File.WriteAllText(_startupTimelineFilePath, value.ToString());
            }
            catch { }
            SettingsChanged?.Invoke();
        }
    }
    public bool NotifyAtStart
    {
        get => Preferences.Get(nameof(NotifyAtStart), true);
        set
        {
            Preferences.Set(nameof(NotifyAtStart), value);
            SettingsChanged?.Invoke();
        }
    }

    public List<NotificationReminder> NotifyBeforeList
    {
        get
        {
            var json = Preferences.Get(nameof(NotifyBeforeList), string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                // Дефолтное значение при первом открытии
                var defaultList = new List<NotificationReminder> { new() { MinutesBefore = 10, IsActive = true } };
                NotifyBeforeList = defaultList;
                return defaultList;
            }
            return JsonSerializer.Deserialize<List<NotificationReminder>>(json) ?? new();
        }
        set
        {
            Preferences.Set(nameof(NotifyBeforeList), JsonSerializer.Serialize(value));
            SettingsChanged?.Invoke();
        }
    }
}