using System.Collections.ObjectModel;
using System.Windows.Input;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.Resources.Strings;

namespace WeeklySchedule.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsService _settingsService;
    private readonly ITimelineRepository _timelineRepository;
    private readonly INotificationService _notificationService;
    private int _refreshVersion;
    private bool _isRefreshing;

    public ObservableCollection<string> ThemeOptions { get; } = [];
    public ObservableCollection<LanguageOption> LanguageOptions { get; } = [];
    public ObservableCollection<Timeline> StartupTimelines { get; } = [];

    private string _selectedTheme = string.Empty;
    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                _settingsService.Theme = value switch
                {
                    var v when v == AppResources.LightTheme => AppTheme.Light,
                    var v when v == AppResources.DarkTheme => AppTheme.Dark,
                    _ => AppTheme.Unspecified
                };
            }
        }
    }

    private LanguageOption? _selectedLanguage;
    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value) && value != null && _settingsService.SelectedLanguage != value.Type)
            {
                _settingsService.SelectedLanguage = value.Type;
                App.RestartApp();
            }
        }
    }

    private int _defaultDuration;
    public int DefaultDuration { get => _defaultDuration; set { if (SetProperty(ref _defaultDuration, value)) _settingsService.DefaultLessonDuration = value; } }

    private bool _openLast;
    public bool OpenLast { get => _openLast; set { if (SetProperty(ref _openLast, value)) { _settingsService.OpenLastTimeline = value; OnPropertyChanged(nameof(IsStartupPickerVisible)); } } }

    public bool IsStartupPickerVisible => !OpenLast;

    private Timeline? _selectedStartupTimeline;
    public Timeline? SelectedStartupTimeline { get => _selectedStartupTimeline; set { if (SetProperty(ref _selectedStartupTimeline, value) && value != null && !_isRefreshing) _settingsService.StartupTimelineId = value.Id; } }

    private bool _areAllPermissionsGranted;
    public bool AreAllPermissionsGranted
    {
        get => _areAllPermissionsGranted;
        set
        {
            if (SetProperty(ref _areAllPermissionsGranted, value))
                OnPropertyChanged(nameof(IsNotPermissionGranted));
        }
    }

    public bool IsNotPermissionGranted => !AreAllPermissionsGranted;

    private bool _notifyAtStart;
    public bool NotifyAtStart { get => _notifyAtStart; set { if (SetProperty(ref _notifyAtStart, value)) _settingsService.NotifyAtStart = value; } }

    public ObservableCollection<NotificationReminderViewModel> ReminderItems { get; } = [];
    public ICommand ToggleOpenLastCommand { get; }
    public ICommand RequestPermissionCommand { get; }
    public ICommand AddReminderCommand { get; }
    public ICommand DeleteReminderCommand { get; }

    public SettingsViewModel(ISettingsService settingsService, ITimelineRepository timelineRepository, INotificationService notificationService)
    {
        _settingsService = settingsService;
        _timelineRepository = timelineRepository;
        _notificationService = notificationService;

        ToggleOpenLastCommand = new Command(() => OpenLast = !OpenLast);
        RequestPermissionCommand = new Command(() => SafeFireAndForget.Run(RequestAllPermissionsAsync));
        AddReminderCommand = new Command(AddReminder);
        DeleteReminderCommand = new Command<NotificationReminderViewModel>(DeleteReminder);

        _defaultDuration = _settingsService.DefaultLessonDuration;
        _openLast = _settingsService.OpenLastTimeline;
        _notifyAtStart = _settingsService.NotifyAtStart;

        LoadThemeOptions();
        LoadLanguageOptions();
        LoadReminders();
    }

    public async Task CheckAllPermissionsAsync()
    {
        AreAllPermissionsGranted = await _notificationService.CheckAllPermissionsAsync();
    }

    private async Task RequestAllPermissionsAsync()
    {
        await _notificationService.RequestAllPermissionsAsync();
        await Task.Delay(500);
        await CheckAllPermissionsAsync();
    }

    private void LoadReminders()
    {
        ReminderItems.Clear();
        foreach (var item in _settingsService.NotifyBeforeList)
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
        _settingsService.NotifyBeforeList = [.. ReminderItems.Select(i => new NotificationReminder { MinutesBefore = i.Minutes, IsActive = i.IsActive })];
    }

    public async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        var all = (await _timelineRepository.GetAllAsync()).ToList();
        if (version != _refreshVersion) return;

        _isRefreshing = true;
        try
        {
            StartupTimelines.Clear();
            foreach (var t in all) StartupTimelines.Add(t);
            _selectedStartupTimeline = all.FirstOrDefault(t => t.Id == _settingsService.StartupTimelineId);
            _openLast = _settingsService.OpenLastTimeline;
            _defaultDuration = _settingsService.DefaultLessonDuration;
            _notifyAtStart = _settingsService.NotifyAtStart;

            LoadReminders();

            OnPropertyChanged(nameof(SelectedStartupTimeline));
            OnPropertyChanged(nameof(OpenLast));
            OnPropertyChanged(nameof(IsStartupPickerVisible));
            OnPropertyChanged(nameof(DefaultDuration));
            OnPropertyChanged(nameof(NotifyAtStart));
        }
        finally { _isRefreshing = false; }

        LoadThemeOptions();
        LoadLanguageOptions();

        await CheckAllPermissionsAsync();
    }

    private void LoadThemeOptions()
    {
        ThemeOptions.Clear();
        ThemeOptions.Add(AppResources.SystemTheme);
        ThemeOptions.Add(AppResources.LightTheme);
        ThemeOptions.Add(AppResources.DarkTheme);

        _selectedTheme = _settingsService.Theme switch
        {
            AppTheme.Light => AppResources.LightTheme,
            AppTheme.Dark => AppResources.DarkTheme,
            _ => AppResources.SystemTheme
        };
        OnPropertyChanged(nameof(SelectedTheme));
    }

    private void LoadLanguageOptions()
    {
        LanguageOptions.Clear();
        var allLangs = new[] { AppLanguage.System, AppLanguage.Russian, AppLanguage.English, AppLanguage.Chinese, AppLanguage.Korean };

        var options = allLangs.Select(lang => new LanguageOption
        {
            Type = lang,
            DisplayName = LanguageHelper.GetDisplayText(lang),
            SortKey = LanguageHelper.GetNeutralName(lang)
        }).ToList();

        var systemOpt = options.First(o => o.Type == AppLanguage.System);
        var sortedOthers = options.Where(o => o.Type != AppLanguage.System).OrderBy(o => o.SortKey).ToList();
        sortedOthers.Insert(0, systemOpt);

        foreach (var opt in sortedOthers) LanguageOptions.Add(opt);

        var currentLang = _settingsService.SelectedLanguage;
        _selectedLanguage = LanguageOptions.FirstOrDefault(o => o.Type == currentLang) ?? LanguageOptions.First();
        OnPropertyChanged(nameof(SelectedLanguage));
    }
}

public class LanguageOption
{
    public AppLanguage Type { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string SortKey { get; set; } = string.Empty;
}

public partial class NotificationReminderViewModel(NotificationReminder model, Action onChanged) : BaseViewModel
{
    private readonly NotificationReminder _model = model;
    private readonly Action _onChanged = onChanged;

    public int Minutes
    {
        get => _model.MinutesBefore;
        set
        {
            var minutes = Math.Clamp(value, 0, 7 * 24 * 60);
            if (_model.MinutesBefore == minutes) return;
            _model.MinutesBefore = minutes;
            OnPropertyChanged();
            _onChanged();
        }
    }
    public bool IsActive { get => _model.IsActive; set { if (_model.IsActive != value) { _model.IsActive = value; OnPropertyChanged(); _onChanged(); } } }
}