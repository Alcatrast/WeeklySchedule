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

    public bool OpenLastTimeline
    {
        get => Preferences.Get(nameof(OpenLastTimeline), true);
        set
        {
            Preferences.Set(nameof(OpenLastTimeline), value);
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>
    /// Значение держится в памяти: геттер дергают FlyoutViewModel.UpdateFlags на каждое
    /// SettingsChanged, AppShell на каждый возврат на главную и RefreshAsync экрана
    /// настроек — а каждое обращение читало файл, в том числе с главного потока посреди
    /// открытия настроек. Писатель у файла один, этот сеттер, так что кэш не разойдется
    /// с диском.
    /// </summary>
    private Guid? _startupTimelineId;

    public Guid StartupTimelineId
    {
        get => _startupTimelineId ??= ReadStartupTimelineId();
        set
        {
            try
            {
                File.WriteAllText(_startupTimelineFilePath, value.ToString());
                // Кэш принимает значение только после удачной записи: иначе геттер стал бы
                // отдавать то, чего на диске нет
                _startupTimelineId = value;
            }
            catch { _startupTimelineId = null; }
            SettingsChanged?.Invoke();
        }
    }

    private Guid ReadStartupTimelineId()
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
    public bool NotifyAtStart
    {
        get => Preferences.Get(nameof(NotifyAtStart), true);
        set
        {
            Preferences.Set(nameof(NotifyAtStart), value);
            SettingsChanged?.Invoke();
        }
    }

    // Напоминание при первом открытии, пока пользователь ничего не настроил
    private static List<NotificationReminder> DefaultReminders() =>
        [new() { MinutesBefore = 10, IsActive = true }];

    public List<NotificationReminder> NotifyBeforeList
    {
        get
        {
            var json = Preferences.Get(nameof(NotifyBeforeList), string.Empty);

            // Дефолт именно возвращаем, а не записываем: запись из геттера дергала бы
            // SettingsChanged и перепланирование уведомлений на ровном месте, в том
            // числе из обработчика самого SettingsChanged
            if (string.IsNullOrEmpty(json)) return DefaultReminders();

            try
            {
                return JsonSerializer.Deserialize<List<NotificationReminder>>(json) ?? DefaultReminders();
            }
            catch (JsonException)
            {
                // Битый JSON в Preferences не должен ронять приложение на старте
                return DefaultReminders();
            }
        }
        set
        {
            Preferences.Set(nameof(NotifyBeforeList), JsonSerializer.Serialize(value));
            SettingsChanged?.Invoke();
        }
    }
}