using System.Collections.ObjectModel;
using System.Windows.Input;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsService _settingsService;
    private readonly ITimelineRepository _timelineRepository;
    private readonly INotificationService _notificationService;
    private int _refreshVersion;
    private bool _isRefreshing;

    public ObservableCollection<string> ThemeOptions { get; } = ["Как в системе", "Светлая", "Темная"];
    public ObservableCollection<Timeline> StartupTimelines { get; } = [];

    private string _selectedTheme;
    public string SelectedTheme { get => _selectedTheme; set { if (SetProperty(ref _selectedTheme, value)) _settingsService.Theme = value switch { "Светлая" => AppTheme.Light, "Темная" => AppTheme.Dark, _ => AppTheme.Unspecified }; } }

    private int _defaultDuration;
    public int DefaultDuration { get => _defaultDuration; set { if (SetProperty(ref _defaultDuration, value)) _settingsService.DefaultLessonDuration = value; } }

    private bool _openLast;
    public bool OpenLast { get => _openLast; set { if (SetProperty(ref _openLast, value)) { _settingsService.OpenLastTimeline = value; OnPropertyChanged(nameof(IsStartupPickerVisible)); } } }

    public bool IsStartupPickerVisible => !OpenLast;

    private Timeline? _selectedStartupTimeline;
    public Timeline? SelectedStartupTimeline { get => _selectedStartupTimeline; set { if (SetProperty(ref _selectedStartupTimeline, value) && value != null && !_isRefreshing) _settingsService.StartupTimelineId = value.Id; } }

    private bool _isPermissionGranted;
    /// <summary>
    /// Разрешение на показ уведомлений — единственное, за которым прячутся настройки.
    /// Точность будильника сюда не входит намеренно: раньше настройки были закрыты,
    /// пока пользователь не отдаст все три разрешения, включая исключение из
    /// оптимизации батареи, — и отказавшийся от него не видел даже списка
    /// напоминаний, хотя уведомления у него работали бы
    /// </summary>
    public bool IsPermissionGranted
    {
        get => _isPermissionGranted;
        set
        {
            if (SetProperty(ref _isPermissionGranted, value))
            {
                OnPropertyChanged(nameof(IsNotPermissionGranted));
                OnPropertyChanged(nameof(ShowInexactAlarmHint));
            }
        }
    }

    public bool IsNotPermissionGranted => !IsPermissionGranted;

    private bool _showOpenNotificationSettings;
    /// <summary>
    /// Отклоненное окончательно разрешение система больше не спрашивает: диалог не
    /// появляется, и кнопка запроса без этого превращалась в нажатие в никуда.
    /// Флаг живет один запуск: на следующем диалог снова стоит попробовать.
    /// </summary>
    public bool ShowOpenNotificationSettings
    {
        get => _showOpenNotificationSettings;
        set
        {
            if (SetProperty(ref _showOpenNotificationSettings, value))
                OnPropertyChanged(nameof(PermissionButtonText));
        }
    }

    public string PermissionButtonText =>
        ShowOpenNotificationSettings ? "Открыть настройки уведомлений" : "Разрешить уведомления";

    private bool _areAlarmsExact = true;
    /// <summary>
    /// Может ли система будить приложение минута в минуту. Без этого уведомления
    /// приходят, но с задержкой, поэтому это подсказка, а не запрет.
    /// </summary>
    public bool AreAlarmsExact
    {
        get => _areAlarmsExact;
        set
        {
            if (SetProperty(ref _areAlarmsExact, value))
                OnPropertyChanged(nameof(ShowInexactAlarmHint));
        }
    }

    public bool ShowInexactAlarmHint => IsPermissionGranted && !AreAlarmsExact;

    private bool _exactAlarmsUnavailable;
    /// <summary>
    /// Системного экрана выдачи точности на прошивке может не быть. Раньше нажатие
    /// в этом случае просто ничего не делало, а исключение уходило в лог.
    /// </summary>
    public bool ExactAlarmsUnavailable
    {
        get => _exactAlarmsUnavailable;
        set => SetProperty(ref _exactAlarmsUnavailable, value);
    }

    private bool _notifyAtStart;
    public bool NotifyAtStart { get => _notifyAtStart; set { if (SetProperty(ref _notifyAtStart, value)) _settingsService.NotifyAtStart = value; } }

    public ObservableCollection<NotificationReminderViewModel> ReminderItems { get; } = new();
    public ICommand ToggleOpenLastCommand { get; }
    public ICommand RequestPermissionCommand { get; }
    public ICommand RequestExactAlarmsCommand { get; }
    public ICommand AddReminderCommand { get; }
    public ICommand DeleteReminderCommand { get; }

    public SettingsViewModel(ISettingsService settingsService, ITimelineRepository timelineRepository, INotificationService notificationService)
    {
        _settingsService = settingsService;
        _timelineRepository = timelineRepository;
        _notificationService = notificationService;

        ToggleOpenLastCommand = new Command(() => OpenLast = !OpenLast);
        // Имя вызывающего передается явно: [CallerMemberName] взял бы его из
        // конструктора, и любой сбой команды писался бы в лог как «[.ctor]»
        RequestPermissionCommand = new Command(() =>
            SafeFireAndForget.Run(RequestNotificationPermissionAsync, nameof(RequestNotificationPermissionAsync)));
        RequestExactAlarmsCommand = new Command(() =>
            SafeFireAndForget.Run(RequestExactAlarmsAsync, nameof(RequestExactAlarmsAsync)));
        AddReminderCommand = new Command(AddReminder);
        DeleteReminderCommand = new Command<NotificationReminderViewModel>(DeleteReminder);

        _selectedTheme = _settingsService.Theme switch { AppTheme.Light => "Светлая", AppTheme.Dark => "Темная", _ => "Как в системе" };
        _defaultDuration = _settingsService.DefaultLessonDuration;
        _openLast = _settingsService.OpenLastTimeline;
        _notifyAtStart = _settingsService.NotifyAtStart;

        LoadReminders();
    }

    /// <summary>
    /// Чтение отделено от применения, потому что RefreshAsync обязана читать до
    /// проверки на устаревание, а присваивать — после нее. Обе половины общие с
    /// <see cref="CheckPermissionsAsync"/>: разойдясь, они дали бы экран, где одно
    /// состояние свежее другого.
    /// </summary>
    private async Task<(bool Granted, bool Exact)> ReadPermissionStateAsync() =>
        (await _notificationService.CheckPermissionAsync(),
         await _notificationService.CanScheduleExactAlarmsAsync());

    private void ApplyPermissionState((bool Granted, bool Exact) state)
    {
        IsPermissionGranted = state.Granted;
        AreAlarmsExact = state.Exact;
    }

    private async Task CheckPermissionsAsync() => ApplyPermissionState(await ReadPermissionStateAsync());

    internal async Task RequestNotificationPermissionAsync()
    {
        // Второе нажатие после отказа ведет в системные настройки: диалога больше не
        // будет, и повторный запрос вернулся бы мгновенно, ничего не показав
        if (ShowOpenNotificationSettings)
        {
            await _notificationService.OpenNotificationSettingsAsync();
            return;
        }

        var granted = await _notificationService.RequestPermissionAsync();
        IsPermissionGranted = granted;
        ShowOpenNotificationSettings = !granted;
    }

    internal async Task RequestExactAlarmsAsync()
    {
        ExactAlarmsUnavailable = !await _notificationService.RequestExactAlarmsAsync();
        // Перечитываем сразу: если точность уже выдана, а подсказка осталась от
        // устаревшего состояния, она должна исчезнуть на самом нажатии, не дожидаясь
        // возврата на экран. Возврат с системного экрана ловит SettingsPage
        ApplyPermissionState(await ReadPermissionStateAsync());
    }

    private void LoadReminders()
    {
        var reminders = _settingsService.NotifyBeforeList;
        if (ReminderItems.Select(r => (r.Minutes, r.IsActive))
            .SequenceEqual(reminders.Select(r => (r.MinutesBefore, r.IsActive)))) return;
        ReminderItems.Clear();
        foreach (var item in reminders)
            ReminderItems.Add(new NotificationReminderViewModel(item, SaveReminders));
    }

    private void AddReminder()
    {
        ReminderItems.Add(new NotificationReminderViewModel(new NotificationReminder { MinutesBefore = 15, IsActive = true }, SaveReminders));
        SaveReminders();
    }

    private void DeleteReminder(NotificationReminderViewModel? item)
    {
        if (item != null)
        {
            ReminderItems.Remove(item);
            SaveReminders();
        }
    }

    private void SaveReminders()
    {
        _settingsService.NotifyBeforeList = ReminderItems.Select(i => new NotificationReminder { MinutesBefore = i.Minutes, IsActive = i.IsActive }).ToList();
    }

    public async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        var all = (await _timelineRepository.GetAllAsync()).ToList();
        var permissions = await ReadPermissionStateAsync();
        if (version != _refreshVersion) return;

        _isRefreshing = true;
        try
        {
            if (!StartupTimelines.Select(t => (t.Id, t.Name, t.NotificationsEnabled))
                .SequenceEqual(all.Select(t => (t.Id, t.Name, t.NotificationsEnabled))))
            {
                StartupTimelines.Clear();
                foreach (var t in all) StartupTimelines.Add(t);
            }
            var startupId = _settingsService.StartupTimelineId;
            SelectedStartupTimeline = StartupTimelines.FirstOrDefault(t => t.Id == startupId);
            if (SetProperty(ref _openLast, _settingsService.OpenLastTimeline, nameof(OpenLast)))
                OnPropertyChanged(nameof(IsStartupPickerVisible));
            SetProperty(ref _selectedTheme, _settingsService.Theme switch { AppTheme.Light => "Светлая", AppTheme.Dark => "Темная", _ => "Как в системе" }, nameof(SelectedTheme));
            SetProperty(ref _defaultDuration, _settingsService.DefaultLessonDuration, nameof(DefaultDuration));
            SetProperty(ref _notifyAtStart, _settingsService.NotifyAtStart, nameof(NotifyAtStart));
            LoadReminders();
            ApplyPermissionState(permissions);
        }
        finally { _isRefreshing = false; }
    }
}

public class NotificationReminderViewModel : BaseViewModel
{
    private readonly NotificationReminder _model;
    private readonly Action _onChanged;

    public NotificationReminderViewModel(NotificationReminder model, Action onChanged)
    {
        _model = model;
        _onChanged = onChanged;
    }

    public int Minutes
    {
        get => _model.MinutesBefore;
        set
        {
            // Не с нуля: BuildNotificationId считает id по паре и числу минут, поэтому
            // напоминание «за 0 минут» получало тот же id, что и уведомление о начале
            // пары, и затирало его. Начало пары — это отдельный переключатель выше
            var minutes = Math.Clamp(value, 1, 7 * 24 * 60);
            if (_model.MinutesBefore == minutes) return;
            _model.MinutesBefore = minutes;
            OnPropertyChanged();
            _onChanged();
        }
    }
    public bool IsActive { get => _model.IsActive; set { if (_model.IsActive != value) { _model.IsActive = value; OnPropertyChanged(); _onChanged(); } } }
}
