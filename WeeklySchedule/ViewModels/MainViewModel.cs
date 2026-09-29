using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using WeeklySchedule.Core;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Messaging;
using WeeklySchedule.Models;
using WeeklySchedule.Resources.Strings;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.ViewModels;

public partial class MainViewModel : BaseViewModel, IDisposable
{
    private readonly ILessonRepository _lessonRepository;
    private readonly ITimelineRepository _timelineRepository;
    private readonly IActiveScheduleService _scheduleService;
    private readonly TimelineScheduler _scheduler;
    private readonly ISettingsService _settingsService;
    private readonly INotificationNavigationService _navService;
    private readonly INotificationSchedulerService _notificationScheduler; // Заменяет INotificationService
    private readonly IDaysWindowManager _daysWindowManager;                // Новый сервис для SRP
    private readonly IEditLessonPageFactory _editLessonPageFactory;

    public Guid ActiveTimelineId => _scheduleService.ActiveTimelineId;

    private string _currentTimelineName = AppResources.Schedule;
    public string CurrentTimelineName
    {
        get => _currentTimelineName;
        set => SetProperty(ref _currentTimelineName, value);
    }

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
        ILessonRepository lessonRepository,
        ITimelineRepository timelineRepository,
        IActiveScheduleService scheduleService,
        ISettingsService settingsService,
        INotificationNavigationService navService,
        INotificationSchedulerService notificationScheduler, // Внедрение через DI
        IDaysWindowManager daysWindowManager,                // Внедрение через DI
        IEditLessonPageFactory editLessonPageFactory)
    {
        _lessonRepository = lessonRepository;
        _timelineRepository = timelineRepository;
        _scheduleService = scheduleService;
        _settingsService = settingsService;
        _scheduler = new TimelineScheduler();
        _navService = navService;
        _notificationScheduler = notificationScheduler;
        _daysWindowManager = daysWindowManager;
        _editLessonPageFactory = editLessonPageFactory;

        _settingsService.SettingsChanged += OnSettingsChanged;
        _navService.NavigationRequested += OnNavigationRequested;
        _scheduleService.ActiveTimelineChanged += OnActiveTimelineChanged;
        _scheduler.OnTimeMarkerReached += OnTimeMarkerReached;
        _scheduler.OnDayChanged += OnDayChanged;

        WeakReferenceMessenger.Default.Register<DataChangedMessage>(this, (r, m) => OnDataChanged(m.Value));
        Application.Current!.RequestedThemeChanged += OnThemeChanged;

        InitializeDays();
    }

    #region Event Handlers
    private void OnSettingsChanged() => SafeFireAndForget.Run(ScheduleAllNotificationsAsync);

    private void OnNavigationRequested() => MainThread.BeginInvokeOnMainThread(CheckPendingNavigation);

    private void OnActiveTimelineChanged(Guid newTimelineId)
    {
        ++_notificationVersion;
        if (_startupCompleted) SafeFireAndForget.Run(ReloadActiveTimelineAsync);
    }
    private void OnDataChanged(DayOfWeek? _) => SafeFireAndForget.Run(ReloadActiveTimelineAsync);

    private void OnTimeMarkerReached(DateTime now)
    {
        Days.FirstOrDefault(d => d.Date == now.Date)?.UpdateLayout(now, _allLessons);
    }

    private void OnDayChanged()
    {
        _scheduler.RebuildQueue();
        RollDaysWindow();
        UpdateAllTitles();
        UpdateAllDays();
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CurrentTheme));
        UpdateAllDays();
    }
    #endregion

    public void Dispose()
    {
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _navService.NavigationRequested -= OnNavigationRequested;
        _scheduleService.ActiveTimelineChanged -= OnActiveTimelineChanged;
        _scheduler.OnTimeMarkerReached -= OnTimeMarkerReached;
        _scheduler.OnDayChanged -= OnDayChanged;

        WeakReferenceMessenger.Default.Unregister<DataChangedMessage>(this);
        Application.Current?.RequestedThemeChanged -= OnThemeChanged;
        _scheduler.Stop();
        GC.SuppressFinalize(this);
    }

    public void CheckPendingNavigation()
    {
        if (!_startupCompleted) return;
        if (_navService.PendingTimelineId.HasValue)
        {
            _scheduleService.ActiveTimelineId = _navService.PendingTimelineId.Value;
            _navService.ClearPendingNavigation();
        }
    }

    public async Task ReloadActiveTimelineAsync()
    {
        var version = ++_loadVersion;
        ++_notificationVersion;

        var timelineId = _scheduleService.ActiveTimelineId;
        var timeline = await _timelineRepository.GetByIdAsync(timelineId);
        var lessons = (await _lessonRepository.GetByTimelineIdAsync(timelineId)).ToList();

        if (version != _loadVersion || timelineId != _scheduleService.ActiveTimelineId) return;

        CurrentTimelineName = timeline?.Name ?? AppResources.Schedule;
        _allLessons = lessons;

        _scheduler.Initialize(_allLessons, TimeContext.Now.Date);

        RollDaysWindow();
        UpdateAllTitles();
        UpdateAllDays();

        await ScheduleAllNotificationsAsync();
    }

    #region Days Window Management (Делегировано IDaysWindowManager)
    private void InitializeDays()
    {
        _daysWindowManager.Initialize(TimeContext.Now.Date);
        SyncDaysCollection();
    }

    private void RollDaysWindow()
    {
        _daysWindowManager.RollWindow(TimeContext.Now.Date);
        SyncDaysCollection();
    }

    private void SyncDaysCollection()
    {
        Days.Clear();
        foreach (var day in _daysWindowManager.Days)
            Days.Add(day);

        if (SelectedDayVM == null || !Days.Contains(SelectedDayVM))
            SelectedDayVM = Days.FirstOrDefault();
    }

    private void UpdateAllTitles()
    {
        _daysWindowManager.UpdateAllTitles(TimeContext.Now);
    }

    private void UpdateAllDays()
    {
        _daysWindowManager.UpdateAllLayouts(TimeContext.Now, _allLessons);
    }
    #endregion

    public async Task InitializeDataAsync()
    {
        await _initGate.WaitAsync();
        try
        {
            if (!_startupCompleted)
            {
                await ApplyStartupTimelineLogicAsync();
                _startupCompleted = true;
            }
            CheckPendingNavigation();
            await EnsureDefaultTimelineExistsAsync();
            await ReloadActiveTimelineAsync();
        }
        finally
        {
            _initGate.Release();
        }
    }

    private async Task ApplyStartupTimelineLogicAsync()
    {
        if (!_settingsService.OpenLastTimeline)
        {
            var startupId = _settingsService.StartupTimelineId;
            var timeline = await _timelineRepository.GetByIdAsync(startupId);
            if (timeline == null)
            {
                var all = await _timelineRepository.GetAllAsync();
                var first = all.FirstOrDefault();
                if (first != null)
                {
                    _scheduleService.ActiveTimelineId = first.Id;
                    _settingsService.StartupTimelineId = first.Id;
                }
            }
            else
            {
                _scheduleService.ActiveTimelineId = timeline.Id;
            }
        }
    }

    private async Task EnsureDefaultTimelineExistsAsync()
    {
        var timelines = (await _timelineRepository.GetAllAsync()).ToList();
        if (timelines.Count == 0)
        {
            var defaultTimeline = new Timeline { Name = AppResources.MySchedule };
            await _timelineRepository.AddAsync(defaultTimeline);
            _scheduleService.ActiveTimelineId = defaultTimeline.Id;
        }
        else
        {
            var checkedId = _scheduleService.ActiveTimelineId;
            var active = await _timelineRepository.GetByIdAsync(checkedId);
            if (checkedId == _scheduleService.ActiveTimelineId && active == null)
                _scheduleService.ActiveTimelineId = timelines.First().Id;
        }
    }

    public void StopMonitor() => _scheduler.Stop();

    #region Notification Scheduling (Делегировано INotificationSchedulerService)
    public async Task ScheduleAllNotificationsAsync()
    {
        var version = ++_notificationVersion;
        var timelineId = _scheduleService.ActiveTimelineId;

        await _notificationScheduler.CancelAllAsync();

        if (version != _notificationVersion) return;

        await _notificationScheduler.ScheduleAllAsync(
            timelineId,
            _settingsService.NotifyAtStart,
            _settingsService.NotifyBeforeList,
            TimeContext.Now);
    }
    #endregion
}