using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Text.Json;
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

    // Сетка общая для всех семи дней, поэтому строится здесь, а не в каждом дне.
    // Экземпляр держится до реальной смены данных: DayView отличает перестройку
    // от обновления состояния по ReferenceEquals, и лишний новый объект стоил бы
    // полной перерисовки с прыжком прокрутки на каждое переименование таймлайна
    private WeekLayout _weekLayout = WeekLayout.Build([], []);
    private (LessonState[] Lessons, BaseDay[] Markers)? _weekSnapshot;
    private sealed record LessonState(Guid Id, DayOfWeek Day, string Name, string Description,
        TimeSpan Start, TimeSpan End, LessonType Type);

    // Один проход инициализации за раз: OnAppearing может прийти повторно,
    // не дождавшись предыдущего
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    // Постановка будильников теперь асинхронна, и между отменой прежних и записью
    // новых появилось место, где два прохода могли бы перемешаться. Раньше их
    // удерживал вместе главный поток
    private readonly SemaphoreSlim _notificationGate = new(1, 1);
    private bool _startupCompleted;
    private int _loadVersion;
    private int _notificationVersion;
    private int _dataRevision;
    private int _loadedRevision = -1;
    private Guid _loadedTimelineId;
    private string? _notificationSettings;
    private string? _notificationData;
    private string? _clockContext;
    private bool _monitorEnabled;
    // Защелка от повторного захода в починку каталога из вложенного вызова
    private bool _recoveringCatalogue;

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
                _selectedDayVM?.UpdateLayout(TimeContext.Now, _weekLayout);
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
        _navService.NavigationRequested += () => MainThread.BeginInvokeOnMainThread(CheckPendingNavigation);

        _scheduleService.ActiveTimelineChanged += OnActiveTimelineChanged;
        _scheduler.OnTimeMarkerReached += (now) => UpdateFromHandler("тик планировщика", () =>
        {
            // Пара началась или закончилась: пересобираем сегодняшний день, иначе
            // подсветка текущей пары остается такой, какой была на прошлом пересчете
            var todayVM = Days.FirstOrDefault(d => d.Date == now.Date);
            todayVM?.UpdateLayout(now, _weekLayout);
        });
        _scheduler.OnDayChanged += () => UpdateFromHandler("смена суток", () =>
        {
            _scheduler.RebuildQueue();
            RollDaysWindow();
            UpdateAllTitles();
            UpdateAllDays();
        });

        AppEvents.DataChanged += OnDataChanged;
        Application.Current!.RequestedThemeChanged += (s, e) => UpdateFromHandler("смена темы", () =>
        {
            OnPropertyChanged(nameof(CurrentTheme));
            UpdateAllDays();
        });

        InitializeDays();
        // Загрузку данных запускает MainPage.OnAppearing. Раньше она стартовала
        // еще и отсюда, и два прохода шли параллельно
    }

    private string NotificationSettingsKey() => $"{_settingsService.NotifyAtStart}:" +
        string.Join(";", _settingsService.NotifyBeforeList.Select(r => $"{r.IsActive}:{r.MinutesBefore}"));

    private void OnSettingsChanged()
    {
        if (_startupCompleted && _notificationSettings != NotificationSettingsKey())
            SafeFireAndForget.Run(() => ScheduleAllNotificationsAsync());
    }

    public void CheckPendingNavigation()
    {
        // На холодном старте запрос ждет завершения выбора стартового расписания.
        if (!_startupCompleted) return;
        if (_navService.PendingTimelineId.HasValue)
        {
            var targetId = _navService.PendingTimelineId.Value;
            _scheduleService.ActiveTimelineId = targetId;
            _navService.ClearPendingNavigation();
        }
    }

    private void OnActiveTimelineChanged(Guid newTimelineId)
    {
        ++_loadVersion;
        ++_notificationVersion;
        if (_startupCompleted) SafeFireAndForget.Run(RefreshWithRecoveryAsync);
    }

    private void OnDataChanged(DayOfWeek? affectedDay)
    {
        ++_dataRevision;
        ++_loadVersion;
        ++_notificationVersion;
        if (_startupCompleted) SafeFireAndForget.Run(RefreshWithRecoveryAsync);
    }

    // Эти два пути идут мимо InitializeDataAsync, поэтому обертку с починкой
    // каталога несут сами. Внутри InitializeCoreAsync вызывается голый
    // RefreshDataIfNeededAsync: вложенные обертки мешали бы друг другу
    private Task RefreshWithRecoveryAsync() => WithCatalogueRecoveryAsync(RefreshDataIfNeededAsync);

    private async Task RefreshDataIfNeededAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            // Сохранение вызывает DataChanged и затем OnAppearing. Оба пути
            // ждут одну актуализацию, а не читают и не ставят будильники дважды.
            while (_loadedTimelineId != ActiveTimelineId || _loadedRevision != _dataRevision)
            {
                await EnsureDefaultTimelineExistsAsync();
                await ReloadActiveTimelineAsync();
            }
        }
        finally { _refreshGate.Release(); }
    }

    /// <summary>
    /// Каталог расписаний мог остаться нечитаемым после сбоя записи. Чтения из
    /// репозитория намеренно бросают, а починка живет на путях записи, поэтому без
    /// этой попытки главный экран молча оставался пустым до конца жизни установки:
    /// исключение уходило в SafeFireAndForget, а записи, которая бы все починила,
    /// никто не начинал.
    /// </summary>
    private async Task WithCatalogueRecoveryAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (JsonException ex)
        {
            // Починка уже идет в параллельной операции — второй заход по тому же
            // файлу дал бы тот же результат
            if (_recoveringCatalogue) throw;
            System.Diagnostics.Debug.WriteLine($"[Каталог] нечитаемый timelines.json: {ex}");

            _recoveringCatalogue = true;
            try
            {
                if (!await _timelineRepository.TryRecoverCorruptedAsync()) throw;

                // Незавершенная загрузка не должна опубликоваться после починки
                ++_loadVersion;
                ++_notificationVersion;
                _loadedRevision = -1;
                await operation();
            }
            finally { _recoveringCatalogue = false; }

            await ReportCatalogueResetAsync();
        }
    }

    private static async Task ReportCatalogueResetAsync()
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Page is Page page)
            await page.DisplayAlertAsync("Список расписаний повреждён",
                "Файл со списком расписаний не читается, рядом с ним сохранена копия. " +
                "Список создан заново — сами пары остались на месте.", "ОК");
    }

    public async Task ReloadActiveTimelineAsync()
    {
        var version = ++_loadVersion;
        var revision = _dataRevision;
        ++_notificationVersion;
        var timelineId = _scheduleService.ActiveTimelineId;
        var timeline = await _timelineRepository.GetByIdAsync(timelineId);
        var lessons = (await _repository.GetByTimelineIdAsync(timelineId)).ToList();
        if (version != _loadVersion || revision != _dataRevision || timelineId != _scheduleService.ActiveTimelineId) return;

        // Название, пары и таймер публикуются вместе, только для актуального запроса.
        CurrentTimelineName = timeline?.Name ?? "Расписание";
        _allLessons = lessons;
        RebuildWeekLayoutIfChanged(timeline?.BaseDays ?? []);
        if (_monitorEnabled) _scheduler.Initialize(_allLessons, TimeContext.Now.Date);
        RollDaysWindow();
        UpdateAllTitles();
        UpdateAllDays();
        // Обработчик LayoutUpdated может упасть в нативной отрисовке. Такой
        // проход не завершён: следующий вход должен повторить загрузку.
        if (version != _loadVersion || revision != _dataRevision || timelineId != ActiveTimelineId) return;
        _loadedTimelineId = timelineId;
        _loadedRevision = revision;
        // Те же расписание и пары, что уже прочитаны выше: без передачи те же файлы
        // читались и разбирались второй раз подряд
        await ScheduleAllNotificationsAsync(timeline, lessons);
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

    /// <summary>
    /// Вызывается при каждом появлении MainPage, в том числе при возврате с модалки.
    /// Разовая часть — демо-данные и выбор стартового таймлайна; остальное нужно
    /// повторять, потому что таймер останавливается в OnDisappearing, а активный
    /// таймлайн мог быть удален на другой странице.
    /// </summary>
    public async Task InitializeDataAsync()
    {
        _monitorEnabled = true;
        // Метод запускался и из конструктора, и из OnAppearing. На первом запуске
        // два прохода видели пустое хранилище и оба сеяли демо-данные: получалось
        // шесть таймлайнов и шестьдесят пар вместо трех и тридцати
        await _initGate.WaitAsync();
        try
        {
            await WithCatalogueRecoveryAsync(InitializeCoreAsync);
        }
        finally
        {
            _initGate.Release();
        }
    }

    private async Task InitializeCoreAsync()
    {
        if (!_startupCompleted)
        {
            await _seeder.SeedAsync(_repository, _timelineRepository, _scheduleService);
            // Боковое меню могло прочитать пустой каталог раньше первого посева.
            AppEvents.NotifyDataChanged();

            // Только на старте: иначе возврат с модалки перебивал бы таймлайн,
            // который пользователь выбрал руками
            await ApplyStartupTimelineLogicAsync();
            _startupCompleted = true;
        }

        CheckPendingNavigation();
        if (_loadedTimelineId != ActiveTimelineId || _loadedRevision != _dataRevision)
        {
            await RefreshDataIfNeededAsync();
        }
        else
        {
            // Возврат из просмотра/редактора без сохранения: данные уже в памяти.
            // Время могло измениться, даже если файлы остались прежними.
            if (_monitorEnabled) _scheduler.Initialize(_allLessons, TimeContext.Now.Date);
            RollDaysWindow();
            UpdateAllTitles();
            UpdateAllDays();
            var clockContext = CurrentClockContext();
            if (_clockContext != clockContext || _notificationSettings != NotificationSettingsKey())
                await ScheduleAllNotificationsAsync();
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
            var defaultTimeline = new Timeline { Name = "Мое расписание" };
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

    private void UpdateAllTitles()
    {
        var now = TimeContext.Now;
        foreach (var dayVM in Days) dayVM.UpdateTitle(now);
    }

    /// <summary>
    /// Перерисовка всех семи дней. Всегда принудительная: сюда приходят загрузка,
    /// смена суток и смена темы, а у последней причина в раскладке не видна — цвет
    /// карточки берется из темы приложения. Плюс предыдущий проход мог оборваться на
    /// ошибке отрисовки, и тогда день остался пустым при тех же данных. Пропускает
    /// лишнюю работу только тик планировщика: он обновляет свой день напрямую.
    /// </summary>
    private void UpdateAllDays()
    {
        var now = TimeContext.Now;
        List<Exception>? errors = null;
        foreach (var dayVM in Days)
        {
            try { dayVM.UpdateLayout(now, _weekLayout, force: true); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
        }
        // Ошибка первого видимого дня не должна оставлять остальные шесть с
        // пустой раскладкой. Ошибки сохраняем для вызывающего кода и логгера.
        if (errors != null) throw new AggregateException("Не удалось отобразить дни расписания.", errors);
    }

    /// <summary>
    /// Обновление дней из синхронного обработчика — тика планировщика или смены темы.
    ///
    /// UpdateAllDays намеренно бросает, чтобы о неудавшейся отрисовке узнал вызывающий
    /// код. На путях загрузки он есть: там await под SafeFireAndForget. Здесь его нет —
    /// оба обработчика приходят на главный поток через BeginInvokeOnMainThread, ловить
    /// исключение оттуда некому, а на Android необработанное исключение главного потока
    /// завершает процесс. Пишем ошибку и живем дальше: DayView на своей стороне уже
    /// сбросил кэш раскладки, поэтому следующий проход построит день заново.
    /// </summary>
    private static void UpdateFromHandler(string source, Action update)
    {
        try
        {
            update();
        }
        catch (Exception ex)
        {
            if (SafeFireAndForget.Logger is { } logger)
                logger.LogError(ex, "[{Source}] не удалось обновить дни расписания", source);
            else
                System.Diagnostics.Debug.WriteLine($"[{source}] не удалось обновить дни расписания: {ex}");
        }
    }

    /// <summary>
    /// Пересобирает общую сетку недели, только если содержимое пар или пометок
    /// действительно изменилось. Переименование таймлайна и возврат на экран
    /// приходят сюда постоянно, а каждая новая раскладка стоит DayView полной
    /// перерисовки: он сравнивает экземпляры по ссылке.
    /// </summary>
    private void RebuildWeekLayoutIfChanged(List<BaseDay> baseDays)
    {
        var snapshot = (
            Lessons: _allLessons.OrderBy(l => l.Id)
                .Select(l => new LessonState(l.Id, l.Day, l.Name, l.Description,
                    l.StartTime, l.EndTime, l.Type)).ToArray(),
            Markers: baseDays.OrderBy(b => b.Day).ThenBy(b => b.StartTime).ThenBy(b => b.Text).ToArray());

        if (_weekSnapshot is { } previous &&
            previous.Lessons.SequenceEqual(snapshot.Lessons) &&
            previous.Markers.SequenceEqual(snapshot.Markers)) return;

        _weekLayout = WeekLayout.Build(_allLessons, baseDays);
        _weekSnapshot = snapshot;
    }

    public void StopMonitor()
    {
        _monitorEnabled = false;
        _scheduler.Stop();
    }

    /// <summary>
    /// Приводит поставленные будильники в соответствие с расписанием и настройками.
    /// Расписание и пары можно передать, если вызывающий их только что прочитал.
    /// </summary>
    public async Task ScheduleAllNotificationsAsync(Timeline? timeline = null, List<Lesson>? lessons = null)
    {
        var version = ++_notificationVersion;
        var timelineId = _scheduleService.ActiveTimelineId;
        if (timeline == null || lessons == null)
        {
            timeline = await _timelineRepository.GetByIdAsync(timelineId);
            lessons = (await _repository.GetByTimelineIdAsync(timelineId)).ToList();
            if (version != _notificationVersion || timelineId != _scheduleService.ActiveTimelineId) return;
        }

        var settingsKey = NotificationSettingsKey();
        var clockContext = CurrentClockContext();
        var dataKey = NotificationDataKey(timeline, lessons);
        // Ничего из того, что попадает в будильник, не изменилось. Переименование
        // расписания и правка длительности пары приходят сюда постоянно, а стоят
        // они отмены и повторной постановки всех будильников до единого
        if (_notificationSettings == settingsKey && _clockContext == clockContext &&
            _notificationData == dataKey) return;

        var plan = BuildNotificationPlan(timeline, lessons);

        await _notificationGate.WaitAsync();
        try
        {
            // До этой точки старый запрос не должен ни отменять, ни ставить будильники.
            if (version != _notificationVersion || timelineId != _scheduleService.ActiveTimelineId) return;

            // Ошибка постановки не должна оставлять признак успешной синхронизации.
            _notificationSettings = null;
            _notificationData = null;
            _clockContext = null;

            await _notificationService.ReplaceScheduledAsync(plan);

            _notificationSettings = settingsKey;
            _notificationData = dataKey;
            _clockContext = clockContext;
        }
        finally { _notificationGate.Release(); }
    }

    private List<PlannedNotification> BuildNotificationPlan(Timeline? timeline, List<Lesson> lessons)
    {
        var plan = new List<PlannedNotification>();
        if (timeline == null || !timeline.NotificationsEnabled) return plan;

        bool notifyAtStart = _settingsService.NotifyAtStart;
        // Строго больше нуля: у напоминания «за 0 минут» id совпадает с id уведомления
        // о начале пары (BuildNotificationId считает его по паре и числу минут), и
        // PendingIntentFlags.UpdateCurrent затирал одно другим — вместо «Начало пары»
        // приходило «Через 0 мин.». Настройки такое значение больше не сохраняют,
        // но в уже сохраненном списке оно могло остаться
        var activeReminders = _settingsService.NotifyBeforeList
            .Where(r => r.IsActive && r.MinutesBefore > 0 && r.MinutesBefore <= 7 * 24 * 60).ToList();

        foreach (var lesson in lessons)
        {
            if (notifyAtStart)
            {
                plan.Add(new PlannedNotification(timeline.Id, lesson.Id,
                    $"Начало пары: {lesson.Name}", lesson.Description,
                    lesson.Day, lesson.StartTime, 0));
            }

            foreach (var reminder in activeReminders)
            {
                plan.Add(new PlannedNotification(timeline.Id, lesson.Id,
                    $"Скоро начнется: {lesson.Name}",
                    $"Через {reminder.MinutesBefore} мин. {lesson.Description}",
                    lesson.Day, lesson.StartTime, reminder.MinutesBefore));
            }
        }
        return plan;
    }

    /// <summary>
    /// Все, что попадает в будильник, одной строкой. Длительность пары и ее тип сюда
    /// не входят: в напоминании их нет. Длины строк выписаны перед ними, иначе
    /// название с разделителем внутри могло бы совпасть с другим набором пар.
    /// </summary>
    private static string NotificationDataKey(Timeline? timeline, List<Lesson> lessons)
    {
        if (timeline == null || !timeline.NotificationsEnabled) return "off";

        var key = new System.Text.StringBuilder(timeline.Id.ToString("N"));
        foreach (var lesson in lessons.OrderBy(l => l.Id))
        {
            key.Append('|').Append(lesson.Id.ToString("N"))
               .Append(':').Append((int)lesson.Day)
               .Append(':').Append(lesson.StartTime.Ticks)
               .Append(':').Append(lesson.Name.Length).Append(':').Append(lesson.Name)
               .Append(':').Append(lesson.Description.Length).Append(':').Append(lesson.Description);
        }
        return key.ToString();
    }

    private static string CurrentClockContext() =>
        $"{TimeZoneInfo.Local.Id}:{TimeZoneInfo.Local.GetUtcOffset(TimeContext.Now)}";
}
