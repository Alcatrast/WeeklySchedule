using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Extensions;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.Messaging;

namespace WeeklySchedule.ViewModels;

public partial class EditTimelineViewModel : BaseViewModel
{
    private readonly ITimelineRepository _repository;
    private readonly ILessonRepository _lessonRepository;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly IFilePickerService _filePickerService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<ExcelMIPTScheduleParser> _logger;
    private readonly Timeline _timeline;
    private readonly bool _isEditMode;
    private bool _isProcessing;

    // Не "дополнить": импорт в существующее расписание заменяет его содержимое
    public string ImportSectionTitle => _isEditMode ? "Заменить импортом" : "Импорт";

    // Повторный разбор возможен, только если исходник сохранен и лежит на месте.
    // У расписаний, заведенных до появления копии файла, кнопки не будет
    public bool CanReimport => _timeline.Source != null && ScheduleSourceStore.Exists(_timeline.Id, _timeline.Source);
    public string ReimportLabel => _timeline.Source is { } source
        ? $"Перечитать файл · {source.GroupName} · {source.ImportedAt:dd.MM.yyyy}"
        : string.Empty;

    private bool _isImporting;
    public bool IsImporting
    {
        get => _isImporting;
        set => SetProperty(ref _isImporting, value);
    }

    public ICommand SelectExcelFileCommand { get; }
    public ICommand ReimportCommand { get; }
    public ICommand ToggleIsStartupCommand { get; }
    public string Title => _isEditMode ? "Редактирование таймлайна" : "Новый таймлайн";
    public bool IsEditMode => _isEditMode;

    private string _name;
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private bool _isStartupTimeline;
    public bool IsStartupTimeline { get => _isStartupTimeline; set => SetProperty(ref _isStartupTimeline, value); }

    private bool _notificationsEnabled;
    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set
        {
            if (SetProperty(ref _notificationsEnabled, value))
                _timeline.NotificationsEnabled = value;
        }
    }

    private bool _showPermissionWarning;
    public bool ShowPermissionWarning
    {
        get => _showPermissionWarning;
        set => SetProperty(ref _showPermissionWarning, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ToggleNotificationsCommand { get; }

    public EditTimelineViewModel(
        ITimelineRepository repository,
        ILessonRepository lessonRepository,
        ISettingsService settingsService,
        INotificationService notificationService,
        IFilePickerService filePickerService,
        INavigationService navigationService,
        IServiceProvider? serviceProvider,
        Timeline? timeline)
    {
        _repository = repository;
        _lessonRepository = lessonRepository;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _filePickerService = filePickerService;
        _navigationService = navigationService;
        // Через необобщенный GetService: обобщенный живет в пакете DI, которого нет
        // в тестовом проекте, а этот файл компилируется и там
        _logger = serviceProvider?.GetService(typeof(ILogger<ExcelMIPTScheduleParser>))
            as ILogger<ExcelMIPTScheduleParser> ?? NullLogger<ExcelMIPTScheduleParser>.Instance;

        _isEditMode = timeline != null;
        _timeline = timeline ?? new Timeline();
        _name = _timeline.Name;

        ToggleIsStartupCommand = new Command(() => IsStartupTimeline = !IsStartupTimeline);
        _isStartupTimeline = _settingsService.StartupTimelineId == _timeline.Id;

        SaveCommand = new Command(() => RunOperation(SaveAsync));
        DeleteCommand = new Command(() => RunOperation(DeleteAsync));
        CancelCommand = new Command(() => RunOperation(_navigationService.PopModalAsync));

        _notificationsEnabled = _timeline.NotificationsEnabled;
        ToggleNotificationsCommand = new Command(() => NotificationsEnabled = !NotificationsEnabled);
        SelectExcelFileCommand = new Command(() => RunOperation(HandleImportAsync));
        ReimportCommand = new Command(() => RunOperation(HandleReimportAsync));
    }

    private void RunOperation(Func<Task> operation) => SafeFireAndForget.Run(async () =>
    {
        if (_isProcessing) return;
        _isProcessing = true;
        try { await operation(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка операции редактора расписания {TimelineId}", _timeline.Id);
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
                await page.DisplayAlertAsync("Ошибка", ex is IncompleteLessonReadException ? ex.Message
                    : "Не удалось завершить операцию. Проверьте доступ к файлам и повторите попытку.", "ОК");
        }
        finally { _isProcessing = false; }
    });

    public async Task CheckPermissionsAsync()
    {
        var granted = await _notificationService.CheckAllPermissionsAsync();
        ShowPermissionWarning = !granted;
    }

    private async Task HandleImportAsync()
    {
        if (IsImporting) return;
        IsImporting = true;
        try
        {
            var file = await _filePickerService.PickExcelFileAsync();
            if (file == null) return;

            // Импорт должен попасть именно в редактируемый таймлайн, поэтому отдаем
            // сам объект: в режиме создания он еще не сохранен в репозитории
            _timeline.Name = (Name ?? string.Empty).Trim();

            // Разбираем не то, что отдал пикер, а свою копию: его путь ведет во
            // временную папку, и на Android ОС вправе ее вычистить прямо посреди
            // работы. Для каждой попытки отдельная копия: отмена не меняет исходник
            // действующего расписания
            string path;
            using (var stream = await file.OpenReadAsync())
                path = await ScheduleSourceStore.StageAsync(_timeline.Id, stream);

            // Передаем управление в View, так как создание страниц с DI лучше делать там
            // Или можно использовать IPageFactory. Для простоты вызываем событие.
            try
            {
                if (ImportRequested is { } requested)
                    requested(path, file.FileName, _timeline, _isEditMode);
                else ScheduleSourceStore.DiscardPending(_timeline.Id, path);
            }
            catch
            {
                ScheduleSourceStore.DiscardPending(_timeline.Id, path);
                throw;
            }
        }
        finally
        {
            IsImporting = false;
        }
    }

    private async Task HandleReimportAsync()
    {
        if (IsImporting || !CanReimport) return;
        IsImporting = true;
        bool dataMayHaveChanged = false;
        try
        {
            var result = await ScheduleReimportService.ReimportAsync(
                _lessonRepository, _repository, _timeline, _logger, () => dataMayHaveChanged = true);

            string group = _timeline.Source?.GroupName ?? string.Empty;
            var (title, message) = result.Status switch
            {
                ReimportStatus.Success => ("Файл перечитан",
                    $"Пар в файле: {result.Parsed}. Записано: {result.Stored}. " +
                    $"Заменено прежних: {result.Removed}." +
                    "\nРасписание теперь совпадает с файлом."),
                ReimportStatus.GroupNotFound => ("Группа не найдена",
                    $"В сохранённом файле больше нет группы «{group}». Расписание не изменено — " +
                    "выберите файл заново через импорт."),
                ReimportStatus.NoLessons => ("Пары не найдены",
                    $"Для группы «{group}» в файле не нашлось ни одной пары. Расписание не изменено." +
                    (result.Skipped > 0
                        ? $" Строк с нераспознанным временем: {result.Skipped}."
                        : string.Empty)),
                ReimportStatus.IncompleteParse => ("Файл разобран не полностью",
                    $"Строк с нераспознанным временем: {result.Skipped}. " +
                    "Расписание не изменено. Исправьте время в файле и повторите импорт."),
                _ => ("Файл недоступен", "Сохранённая копия файла не найдена. Импортируйте расписание заново.")
            };

            if (result.Status == ReimportStatus.Success)
            {
                OnPropertyChanged(nameof(ReimportLabel));
                dataMayHaveChanged = false;
                AppEvents.NotifyDataChanged();
            }
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null) await page.DisplayAlertAsync(title, message, "ОК");
        }
        finally
        {
            IsImporting = false;
            if (dataMayHaveChanged) AppEvents.NotifyDataChanged();
        }
    }

    /// <summary>
    /// Путь к сохраненной копии, исходное имя файла, редактируемый таймлайн,
    /// признак того что таймлайн уже есть в репозитории.
    /// </summary>
    public event Action<string, string, Timeline, bool>? ImportRequested;

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            if (Application.Current?.Windows.FirstOrDefault()?.Page is Page page)
                await page.DisplayAlertAsync("Ошибка", "Введите название таймлайна", "ОК");
            return;
        }

        _timeline.Name = Name.Trim();
        if (_isEditMode) await _repository.UpdateAsync(_timeline);
        else await _repository.AddAsync(_timeline);

        ApplyStartupSelection();
        AppEvents.NotifyDataChanged();
        await _navigationService.PopModalAsync();
    }

    // Вызывается также после успешного импорта: тот закрывает редактор без SaveAsync.
    public void ApplyStartupSelection()
    {
        if (IsStartupTimeline)
        {
            _settingsService.StartupTimelineId = _timeline.Id;
            _settingsService.OpenLastTimeline = false;
        }
        else if (_settingsService.StartupTimelineId == _timeline.Id)
        {
            _settingsService.StartupTimelineId = Guid.Empty;
        }

    }

    private async Task DeleteAsync()
    {
        bool confirm = await ItemActions.DeleteTimelineAsync(_timeline);

        if (confirm)
        {
            await _navigationService.PopModalAsync();
        }
    }
}
