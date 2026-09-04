using System.Collections.ObjectModel;
using WeeklySchedule.Core;
using WeeklySchedule.Data;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Messaging;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly ILessonRepository _repository;
    private readonly ITimelineRepository _timelineRepository;
    private readonly IDataSeeder _seeder;
    private readonly IActiveScheduleService _scheduleService;
    private readonly TimelineScheduler _scheduler;
    private readonly ISettingsService _settingsService;
    private readonly INotificationNavigationService _navService;
    private readonly INotificationService _notificationService;

    public Guid ActiveTimelineId => _scheduleService.ActiveTimelineId;

    private string _currentTimelineName = "Расписание";
    public string CurrentTimelineName
    {
        get => _currentTimelineName;
        set => SetProperty(ref _currentTimelineName, value);
    }

    private List<Lesson> _allLessons = [];
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
        INotificationService notificationService)
    {
        _repository = repository;
        _timelineRepository = timelineRepository;
        _seeder = seeder;
        _scheduleService = scheduleService;
        _settingsService = settingsService;
        _scheduler = new TimelineScheduler();
        _navService = navService;
        _notificationService = notificationService;

        _settingsService.SettingsChanged += OnSettingsChanged;
        CheckPendingNavigation();

        _scheduleService.ActiveTimelineChanged += OnActiveTimelineChanged;
        _scheduler.OnTimeMarkerReached += (now) =>
        {
            // Пара началась или закончилась: пересобираем сегодняшний день, иначе
            // подсветка текущей пары остается такой, какой была на прошлом пересчете
            var todayVM = Days.FirstOrDefault(d => d.Date == now.Date);
            todayVM?.UpdateLayout(now, _allLessons);
        };
        _scheduler.OnDayChanged += () =>
        {
            _scheduler.RebuildQueue();
            RollDaysWindow();
            UpdateAllTitles();
            UpdateAllDays();
        };

        AppEvents.DataChanged += OnDataChanged;
        Application.Current!.RequestedThemeChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(CurrentTheme));
            UpdateAllDays();
        };

        InitializeDays();
        // ИСПРАВЛЕНО: Запуск асинхронной инициализации
        _ = InitializeDataAsync();
    }

    private void OnSettingsChanged() => _ = ScheduleAllNotificationsAsync();

    public void CheckPendingNavigation()
    {
        if (_navService.PendingTimelineId.HasValue)
        {
            var targetId = _navService.PendingTimelineId.Value;
            _scheduleService.ActiveTimelineId = targetId;
            _navService.ClearPendingNavigation();
        }
    }

    private async void OnActiveTimelineChanged(Guid newTimelineId)
    {
        var timeline = await _timelineRepository.GetByIdAsync(newTimelineId);
        CurrentTimelineName = timeline?.Name ?? "Расписание";
        _allLessons = [.. await _repository.GetByTimelineIdAsync(newTimelineId)];
        _scheduler.Initialize(_allLessons, TimeContext.Now.Date);
        UpdateAllDays();
        await ScheduleAllNotificationsAsync();
    }

    private async void OnDataChanged(DayOfWeek? affectedDay)
    {
        _allLessons = [.. await _repository.GetByTimelineIdAsync(_scheduleService.ActiveTimelineId)];
        _scheduler.RebuildQueue();
        var now = TimeContext.Now;

        if (affectedDay.HasValue)
        {
            var targetVM = Days.FirstOrDefault(d => d.DayOfWeek == affectedDay.Value);
            targetVM?.UpdateLayout(now, _allLessons);
            var todayVM = Days.FirstOrDefault(d => d.Date == now.Date);
            if (todayVM != targetVM) todayVM?.UpdateLayout(now, _allLessons);
            if (targetVM == SelectedDayVM) targetVM?.RequestScroll();
        }
        else
        {
            UpdateAllDays();
        }
        await ScheduleAllNotificationsAsync();
    }

    private void InitializeDays()
    {
        Days.Clear();
        var today = TimeContext.Now.Date;
        for (int i = 0; i < 7; i++)
        {
            var day = new DayViewModel(today.AddDays(i));
            day.UpdateTitle(TimeContext.Now);
            Days.Add(day);
        }
        SelectedDayVM = Days[0];
    }

    /// <summary>
    /// Сдвигает окно из 7 дней после смены суток: выбрасывает прошедшие дни
    /// и достраивает недостающие в конец, сохраняя объекты оставшихся дней.
    /// </summary>
    private void RollDaysWindow()
    {
        var today = TimeContext.Now.Date;

        while (Days.Count > 0 && Days[0].Date < today) Days.RemoveAt(0);

        // Приложение пролежало в фоне неделю и больше — окно проще собрать заново
        if (Days.Count == 0)
        {
            InitializeDays();
            return;
        }

        while (Days.Count < 7) Days.Add(new DayViewModel(Days[^1].Date.AddDays(1)));

        // Выбранным мог быть день, который только что выпал из окна
        if (SelectedDayVM == null || !Days.Contains(SelectedDayVM))
            SelectedDayVM = Days[0];
    }

    public async Task InitializeDataAsync()
    {
        // ИСПРАВЛЕНО: Вызов сидера и логики старта внутри асинхронного метода
        await _seeder.SeedAsync(_repository, _timelineRepository, _scheduleService);
        await ApplyStartupTimelineLogicAsync();
        await EnsureDefaultTimelineExistsAsync();

        var activeTimeline = await _timelineRepository.GetByIdAsync(_scheduleService.ActiveTimelineId);
        CurrentTimelineName = activeTimeline?.Name ?? "Расписание";
        _allLessons = [.. await _repository.GetByTimelineIdAsync(_scheduleService.ActiveTimelineId)];
        _scheduler.Initialize(_allLessons, TimeContext.Now.Date);

        // Приложение могло пролежать в фоне через полночь: тогда таймер был остановлен
        // и OnDayChanged не придет — окно дней надо сдвинуть здесь
        RollDaysWindow();
        UpdateAllTitles();
        UpdateAllDays();
        await ScheduleAllNotificationsAsync();
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
            var defaultTimeline = new Timeline { Name = "Мое расписание" };
            await _timelineRepository.AddAsync(defaultTimeline);
            _scheduleService.ActiveTimelineId = defaultTimeline.Id;
        }
        else if (_scheduleService.ActiveTimelineId == Guid.Empty ||
                 await _timelineRepository.GetByIdAsync(_scheduleService.ActiveTimelineId) == null)
        {
            _scheduleService.ActiveTimelineId = timelines.First().Id;
        }
    }

    private void UpdateAllTitles()
    {
        var now = TimeContext.Now;
        foreach (var dayVM in Days) dayVM.UpdateTitle(now);
    }

    private void UpdateAllDays()
    {
        var now = TimeContext.Now;
        foreach (var dayVM in Days) dayVM.UpdateLayout(now, _allLessons);
    }

    public void StopMonitor() => _scheduler.Stop();

    public async Task ScheduleAllNotificationsAsync()
    {
        _notificationService.CancelAllNotifications();
        var timeline = await _timelineRepository.GetByIdAsync(_scheduleService.ActiveTimelineId);

        if (timeline == null || !timeline.NotificationsEnabled) return;

        bool notifyAtStart = _settingsService.NotifyAtStart;
        var activeReminders = _settingsService.NotifyBeforeList.Where(r => r.IsActive).ToList();

        if (!notifyAtStart && activeReminders.Count == 0) return;

        var now = TimeContext.Now;
        foreach (var lesson in _allLessons)
        {
            DateTime nextDate = GetNextDateForDayOfWeek(lesson.Day, now.Date);
            DateTime lessonStartDateTime = nextDate.Add(lesson.StartTime);

            if (notifyAtStart && lessonStartDateTime > now)
            {
                _notificationService.ScheduleNotification(
                    timeline.Id, lesson.Id,
                    $"Начало пары: {lesson.Name}", lesson.Description,
                    lessonStartDateTime, 0);
            }

            foreach (var reminder in activeReminders)
            {
                DateTime triggerTime = lessonStartDateTime.AddMinutes(-reminder.MinutesBefore);
                if (triggerTime > now)
                {
                    _notificationService.ScheduleNotification(
                        timeline.Id, lesson.Id,
                        $"Скоро начнется: {lesson.Name}",
                        $"Через {reminder.MinutesBefore} мин. {lesson.Description}",
                        lessonStartDateTime, reminder.MinutesBefore);
                }
            }
        }
    }

    private DateTime GetNextDateForDayOfWeek(DayOfWeek targetDay, DateTime fromDate)
    {
        int daysUntil = ((int)targetDay - (int)fromDate.DayOfWeek + 7) % 7;
        return fromDate.AddDays(daysUntil).Date;
    }
}