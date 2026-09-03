using System.Collections.ObjectModel;
using System.Windows.Input;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Services;

namespace WeeklySchedule.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsService _settingsService;
    private readonly ITimelineRepository _timelineRepository;
    private readonly INotificationService _notificationService;

    public ObservableCollection<string> ThemeOptions { get; } = ["Как в системе", "Светлая", "Темная"];
    public ObservableCollection<string> LanguageOptions { get; } = ["Русский", "English"];
    public ObservableCollection<Timeline> StartupTimelines { get; } = [];

    private string _selectedTheme;
    public string SelectedTheme { get => _selectedTheme; set { if (SetProperty(ref _selectedTheme, value)) _settingsService.Theme = value switch { "Светлая" => AppTheme.Light, "Темная" => AppTheme.Dark, _ => AppTheme.Unspecified }; } }

    private int _defaultDuration;
    public int DefaultDuration { get => _defaultDuration; set { if (SetProperty(ref _defaultDuration, value)) _settingsService.DefaultLessonDuration = value; } }

    private string _selectedLanguage;
    public string SelectedLanguage { get => _selectedLanguage; set { if (SetProperty(ref _selectedLanguage, value)) _settingsService.Language = value == "Русский" ? AppLanguage.Russian : AppLanguage.English; } }

    private bool _openLast;
    public bool OpenLast { get => _openLast; set { if (SetProperty(ref _openLast, value)) { _settingsService.OpenLastTimeline = value; OnPropertyChanged(nameof(IsStartupPickerVisible)); } } }

    public bool IsStartupPickerVisible => !OpenLast;

    private Timeline? _selectedStartupTimeline;
    public Timeline? SelectedStartupTimeline { get => _selectedStartupTimeline; set { if (SetProperty(ref _selectedStartupTimeline, value) && value != null) _settingsService.StartupTimelineId = value.Id; } }

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

    public ObservableCollection<NotificationReminderViewModel> ReminderItems { get; } = new();
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
        RequestPermissionCommand = new Command(async () => await RequestAllPermissionsAsync());
        AddReminderCommand = new Command(AddReminder);
        DeleteReminderCommand = new Command<NotificationReminderViewModel>(DeleteReminder);

        _selectedTheme = _settingsService.Theme switch { AppTheme.Light => "Светлая", AppTheme.Dark => "Темная", _ => "Как в системе" };
        _defaultDuration = _settingsService.DefaultLessonDuration;
        _selectedLanguage = _settingsService.Language == AppLanguage.Russian ? "Русский" : "English";
        _openLast = _settingsService.OpenLastTimeline;
        _notifyAtStart = _settingsService.NotifyAtStart;

        LoadReminders();
        _ = LoadStartupTimelinesAsync();
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
        _settingsService.NotifyBeforeList = ReminderItems.Select(i => new NotificationReminder { MinutesBefore = i.Minutes, IsActive = i.IsActive }).ToList();
    }

    private async Task LoadStartupTimelinesAsync()
    {
        StartupTimelines.Clear();
        var all = await _timelineRepository.GetAllAsync();
        foreach (var t in all) StartupTimelines.Add(t);

        var savedId = _settingsService.StartupTimelineId;
        _selectedStartupTimeline = StartupTimelines.FirstOrDefault(t => t.Id == savedId) ?? StartupTimelines.FirstOrDefault();
        if (_selectedStartupTimeline != null && _settingsService.StartupTimelineId != _selectedStartupTimeline.Id)
            _settingsService.StartupTimelineId = _selectedStartupTimeline.Id;

        OnPropertyChanged(nameof(SelectedStartupTimeline));
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

    public int Minutes { get => _model.MinutesBefore; set { if (_model.MinutesBefore != value) { _model.MinutesBefore = value; OnPropertyChanged(); _onChanged(); } } }
    public bool IsActive { get => _model.IsActive; set { if (_model.IsActive != value) { _model.IsActive = value; OnPropertyChanged(); _onChanged(); } } }
}