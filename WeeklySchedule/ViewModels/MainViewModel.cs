using System.Collections.ObjectModel;
using WeeklySchedule.Core;
using WeeklySchedule.Data;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Messaging;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.ViewModels;

public partial class MainViewModel : BaseViewModel, IDisposable
{
    private readonly ILessonRepository _repository;
    private readonly ITimelineRepository _timelineRepository;
    private readonly IDataSeeder _seeder;
    private readonly IActiveScheduleService _scheduleService;
    private readonly TimelineScheduler _scheduler;
    private readonly ISettingsService _settingsService;
    private readonly INotificationNavigationService _navService;
    private readonly INotificationService _notificationService;
    private readonly IEditLessonPageFactory _editLessonPageFactory;

    public Guid ActiveTimelineId => _scheduleService.ActiveTimelineId;
    private string _currentTimelineName = "Расписание";
    public string CurrentTimelineName { get => _currentTimelineName; set => SetProperty(ref _currentTimelineName, value); }

    private List<Lesson> _allLessons = [];
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _startupCompleted;
    private int _loadVersion;
    private int _notificationVersion;

    public ObservableCollection<DayViewModel> Days { get; } = [];
    private DayViewModel? _selectedDayVM;
    public DayViewModel? SelectedDayVM
    {
        get => _selectedDayVM;
        set
        {
            if (SetProperty(ref _selectedDayVM, value))
            {
                _selectedDayVM?.UpdateTitle(TimeContext.Now);
                _selectedDayVM?.UpdateLayout(TimeContext.Now, _allLessons);
                _selectedDayVM?.RequestScroll();
            }
        }
    }

    public static AppTheme CurrentTheme => Application.Current?.RequestedTheme ?? AppTheme.Light;

    public MainViewModel(
        ILessonRepository repository,
        ITimelineRepository timelineRepository,
        IDataSeeder seeder,
        IActiveScheduleService scheduleService,
        ISettingsService settingsService,
        INotificationNavigationService navService,
        INotificationService notificationService,
        IEditLessonPageFactory editLessonPageFactory)
    {
        _repository = repository;
        _timelineRepository = timelineRepository;
        _seeder = seeder;
        _scheduleService = scheduleService;
        _settingsService = settingsService;
        _scheduler = new TimelineScheduler();
        _navService = navService;
        _notificationService = notificationService;
        _editLessonPageFactory = editLessonPageFactory;

        _settingsService.SettingsChanged += OnSettingsChanged;
        _navService.NavigationRequested += OnNavigationRequested;
        _scheduleService.ActiveTimelineChanged += OnActiveTimelineChanged;
        _scheduler.OnTimeMarkerReached += OnTimeMarkerReached;
        _scheduler.OnDayChanged += OnDayChanged;
        AppEvents.DataChanged += OnDataChanged;
        Application.Current!.RequestedThemeChanged += OnThemeChanged;

        InitializeDays();
    }

    private void OnSettingsChanged() => SafeFireAndForget.Run(ScheduleAllNotificationsAsync);
    private void OnNavigationRequested() => MainThread.BeginInvokeOnMainThread(CheckPendingNavigation);
    private void OnActiveTimelineChanged(Guid newTimelineId) { ++_notificationVersion; if (_startupCompleted) SafeFireAndForget.Run(ReloadActiveTimelineAsync); }
    private void OnDataChanged(DayOfWeek? affectedDay) => SafeFireAndForget.Run(ReloadActiveTimelineAsync);
    private void OnTimeMarkerReached(DateTime now) => Days.FirstOrDefault(d => d.Date == now.Date)?.UpdateLayout(now, _allLessons);
    private void OnDayChanged() { _scheduler.RebuildQueue(); RollDaysWindow(); UpdateAllTitles(); UpdateAllDays(); }
    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) { OnPropertyChanged(nameof(CurrentTheme)); UpdateAllDays(); }

    public void Dispose()
    {
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _navService.NavigationRequested -= OnNavigationRequested;
        _scheduleService.ActiveTimelineChanged -= OnActiveTimelineChanged;
        _scheduler.OnTimeMarkerReached -= OnTimeMarkerReached;
        _scheduler.OnDayChanged -= OnDayChanged;
        AppEvents.DataChanged -= OnDataChanged;
        if (Application.Current != null) Application.Current.RequestedThemeChanged -= OnThemeChanged;
        _scheduler.Stop();
    }

    public void CheckPendingNavigation() { if (!_startupCompleted) return; if (_navService.PendingTimelineId.HasValue) { _scheduleService.ActiveTimelineId = _navService.PendingTimelineId.Value; _navService.ClearPendingNavigation(); } }

    public async Task ReloadActiveTimelineAsync()
    {
        var version = ++_loadVersion; ++_notificationVersion;
        var timelineId = _scheduleService.ActiveTimelineId;
        var timeline = await _timelineRepository.GetByIdAsync(timelineId);
        var lessons = (await _repository.GetByTimelineIdAsync(timelineId)).ToList();
        if (version != _loadVersion || timelineId != _scheduleService.ActiveTimelineId) return;
        CurrentTimelineName = timeline?.Name ?? "Расписание";
        _allLessons = lessons;
        _scheduler.Initialize(_allLessons, TimeContext.Now.Date);
        RollDaysWindow(); UpdateAllTitles(); UpdateAllDays();
        await ScheduleAllNotificationsAsync();
    }

    private void InitializeDays() { Days.Clear(); var today = TimeContext.Now.Date; for (int i = 0; i < 7; i++) { var day = new DayViewModel(today.AddDays(i), _scheduleService.ActiveTimelineId, _editLessonPageFactory); day.UpdateTitle(TimeContext.Now); Days.Add(day); } SelectedDayVM = Days[0]; }
    private void RollDaysWindow() { var today = TimeContext.Now.Date; while (Days.Count > 0 && Days[0].Date < today) Days.RemoveAt(0); if (Days.Count == 0) { InitializeDays(); return; } while (Days.Count < 7) Days.Add(new DayViewModel(Days[^1].Date.AddDays(1), _scheduleService.ActiveTimelineId, _editLessonPageFactory)); if (SelectedDayVM == null || !Days.Contains(SelectedDayVM)) SelectedDayVM = Days[0]; }

    public async Task InitializeDataAsync()
    {
        await _initGate.WaitAsync();
        try { if (!_startupCompleted) { await _seeder.SeedAsync(_repository, _timelineRepository, _scheduleService); await ApplyStartupTimelineLogicAsync(); _startupCompleted = true; } CheckPendingNavigation(); await EnsureDefaultTimelineExistsAsync(); await ReloadActiveTimelineAsync(); }
        finally { _initGate.Release(); }
    }

    private async Task ApplyStartupTimelineLogicAsync() { if (!_settingsService.OpenLastTimeline) { var startupId = _settingsService.StartupTimelineId; var timeline = await _timelineRepository.GetByIdAsync(startupId); if (timeline == null) { var all = await _timelineRepository.GetAllAsync(); var first = all.FirstOrDefault(); if (first != null) { _scheduleService.ActiveTimelineId = first.Id; _settingsService.StartupTimelineId = first.Id; } } else { _scheduleService.ActiveTimelineId = timeline.Id; } } }
    private async Task EnsureDefaultTimelineExistsAsync() { var timelines = (await _timelineRepository.GetAllAsync()).ToList(); if (timelines.Count == 0) { var defaultTimeline = new Timeline { Name = "Мое расписание" }; await _timelineRepository.AddAsync(defaultTimeline); _scheduleService.ActiveTimelineId = defaultTimeline.Id; } else { var checkedId = _scheduleService.ActiveTimelineId; var active = await _timelineRepository.GetByIdAsync(checkedId); if (checkedId == _scheduleService.ActiveTimelineId && active == null) _scheduleService.ActiveTimelineId = timelines.First().Id; } }

    private void UpdateAllTitles() { var now = TimeContext.Now; foreach (var dayVM in Days) dayVM.UpdateTitle(now); }
    private void UpdateAllDays() { var now = TimeContext.Now; foreach (var dayVM in Days) dayVM.UpdateLayout(now, _allLessons); }
    public void StopMonitor() => _scheduler.Stop();

    public async Task ScheduleAllNotificationsAsync()
    {
        var version = ++_notificationVersion; var timelineId = _scheduleService.ActiveTimelineId;
        var timeline = await _timelineRepository.GetByIdAsync(timelineId);
        var lessons = (await _repository.GetByTimelineIdAsync(timelineId)).ToList();
        if (version != _notificationVersion || timelineId != _scheduleService.ActiveTimelineId) return;
        _notificationService.CancelAllNotifications();
        if (timeline == null || !timeline.NotificationsEnabled) return;
        bool notifyAtStart = _settingsService.NotifyAtStart;
        var activeReminders = _settingsService.NotifyBeforeList.Where(r => r.IsActive && r.MinutesBefore >= 0 && r.MinutesBefore <= 7 * 24 * 60).ToList();
        if (!notifyAtStart && activeReminders.Count == 0) return;
        var now = TimeContext.Now;
        foreach (var lesson in lessons)
        {
            if (notifyAtStart) _notificationService.ScheduleNotification(timeline.Id, lesson.Id, $"Начало пары: {lesson.Name}", lesson.Description, GetNextOccurrence(lesson, now), 0);
            foreach (var reminder in activeReminders) { var start = GetNextOccurrence(lesson, now.AddMinutes(reminder.MinutesBefore)); _notificationService.ScheduleNotification(timeline.Id, lesson.Id, $"Скоро начнется: {lesson.Name}", $"Через {reminder.MinutesBefore} мин. {lesson.Description}", start, reminder.MinutesBefore); }
        }
    }

    private static DateTime GetNextOccurrence(Lesson lesson, DateTime from) { int daysUntil = ((int)lesson.Day - (int)from.DayOfWeek + 7) % 7; var occurrence = from.Date.AddDays(daysUntil).Add(lesson.StartTime); return occurrence > from ? occurrence : occurrence.AddDays(7); }
}