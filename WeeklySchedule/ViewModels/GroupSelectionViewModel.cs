using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.ObjectModel;
using System.Windows.Input;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Extensions;
using WeeklySchedule.Messaging;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.ViewModels;

public partial class GroupSelectionViewModel : BaseViewModel
{
    // Путь к своей копии файла, а не к тому, что отдал системный пикер: OnAppearing
    // перечитывает файл при каждом возврате на экран, а копия в кэше приложения к
    // этому моменту могла быть вычищена системой
    private readonly string _filePath;
    private readonly string _sourceFileName;
    // Состояние меняется и при незавершённой попытке: повтор уже обновляет каталог.
    private bool _timelineSaved;
    private bool _sourceCommitted;
    private readonly Timeline _timeline;
    private readonly ILessonRepository _lessonRepo;
    private readonly ITimelineRepository _timelineRepo;
    private readonly INavigationService _navigationService;
    private readonly ILogger<ExcelMIPTScheduleParser> _logger;
    private readonly Action? _onImported;

    private GroupItem? _selectedGroup;
    public ObservableCollection<GroupCategory> Categories { get; } = [];
    public ICommand ToggleCategoryCommand { get; }
    public ICommand SelectGroupCommand { get; }

    private bool _isLoadingGroups;
    public bool IsLoadingGroups
    {
        get => _isLoadingGroups;
        set => SetProperty(ref _isLoadingGroups, value);
    }

    private bool _isProcessing;
    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetProperty(ref _isProcessing, value);
    }

    public GroupSelectionViewModel(
        string filePath,
        string sourceFileName,
        bool timelineExists,
        Timeline timeline,
        ILessonRepository lessonRepo,
        ITimelineRepository timelineRepo,
        INavigationService navigationService,
        IServiceProvider serviceProvider,
        Action? onImported = null)
    {
        _filePath = filePath;
        _sourceFileName = sourceFileName;
        _timelineSaved = timelineExists;
        _timeline = timeline;
        _lessonRepo = lessonRepo;
        _timelineRepo = timelineRepo;
        _navigationService = navigationService;
        // Раньше парсер получал NullLogger, и при разборе чужого файла не оставалось
        // никакой диагностики — ни ненайденной группы, ни числа прочитанных пар
        _logger = serviceProvider?.GetService(typeof(ILogger<ExcelMIPTScheduleParser>))
            as ILogger<ExcelMIPTScheduleParser> ?? NullLogger<ExcelMIPTScheduleParser>.Instance;
        _onImported = onImported;

        ToggleCategoryCommand = new Command<GroupCategory>(ToggleCategory);
        SelectGroupCommand = new Command<GroupItem>(SelectGroup);
        IsLoadingGroups = true;
    }

    /// <summary>
    /// OnAppearing приходит не только на первом показе: возврат приложения из фона
    /// на этой же странице запускает разбор повторно. Без сброса списка категории
    /// и группы задваивались, а без флага загрузки оверлей не появлялся и тапы
    /// проходили прямо во время повторного чтения файла.
    /// </summary>
    public async Task InitializeAsync()
    {
        IsLoadingGroups = true;
        Categories.Clear();
        _selectedGroup = null;
        try
        {
            var groups = await Task.Run(() =>
            {
                var parser = new ExcelMIPTScheduleParser(_logger);
                return parser.ExtractAllGroupNames(_filePath);
            });

            if (groups.Count == 0)
            {
                await ShowErrorAndCloseAsync("Не удалось найти ни одной группы в файле.");
                return;
            }

            var dict = new Dictionary<string, List<GroupItem>>();
            foreach (var g in groups)
            {
                var parts = g.Split(new[] { '-' }, 2);
                if (parts.Length != 2) continue;
                var prefix = parts[0].Trim();
                var suffix = parts[1].Trim();
                if (!dict.ContainsKey(prefix)) dict[prefix] = [];
                dict[prefix].Add(new GroupItem { FullGroupName = g, Suffix = suffix });
            }

            foreach (var kvp in dict)
            {
                Categories.Add(new GroupCategory
                {
                    Prefix = kvp.Key,
                    Groups = new ObservableCollection<GroupItem>(kvp.Value)
                });
            }
        }
        catch (Exception)
        {
            await ShowErrorAndCloseAsync("Ошибка при чтении файла. Убедитесь, что формат корректен.");
        }
        finally
        {
            IsLoadingGroups = false;
        }
    }

    private void ToggleCategory(GroupCategory? category)
    {
        if (category == null || IsProcessing || IsLoadingGroups) return;
        foreach (var c in Categories) c.IsExpanded = (c == category);
    }

    private void SelectGroup(GroupItem? group)
    {
        if (group == null || IsProcessing || IsLoadingGroups) return;
        if (_selectedGroup == group)
            SafeFireAndForget.Run(() => ImportGroupAsync(group));
        else
        {
            if (_selectedGroup != null) _selectedGroup.IsSelected = false;
            _selectedGroup = group;
            group.IsSelected = true;
        }
    }

    private async Task ImportGroupAsync(GroupItem group)
    {
        if (IsProcessing) return;
        IsProcessing = true;
        bool dataMayHaveChanged = false;
        try
        {
            // Импорт заменяет содержимое расписания, а кнопка называется импортом:
            // без вопроса пары, заведенные руками, исчезли бы молча
            _timelineSaved |= await _timelineRepo.GetByIdAsync(_timeline.Id) != null;
            int existing = (await _lessonRepo.GetByTimelineIdForReplacementAsync(_timeline.Id)).Count();
            if (existing > 0 && !await ConfirmReplaceAsync(group.FullGroupName, existing)) return;

            var parsed = await Task.Run(() =>
            {
                var parser = new ExcelMIPTScheduleParser(_logger);
                var lessons = parser.ParseGroupSchedule(_filePath, group.FullGroupName,
                    out var baseDays, out int skipped);
                return (Lessons: lessons, BaseDays: baseDays, Skipped: skipped);
            });
            var lessons = parsed.Lessons;
            if (parsed.Skipped > 0)
            {
                await ShowAlertAsync("Файл разобран не полностью",
                    $"Строк с нераспознанным временем: {parsed.Skipped}. " +
                    "Расписание не изменено. Исправьте время в файле и повторите импорт.");
                return;
            }
            // Пустой разбор при выбранной группе — поломка формата куда чаще, чем
            // настоящее пустое расписание. Пока импорт копил пары, такой разбор был
            // безобиден; замена стерла бы расписание им же. Та же проверка стоит на
            // пути повторного разбора — ScheduleReimportService, ReimportStatus.NoLessons
            if (lessons.Count == 0 && existing > 0)
            {
                await ShowAlertAsync("Пары не найдены",
                    $"Для группы «{group.FullGroupName}» в файле не нашлось ни одной пары. " +
                    "Расписание не изменено.");
                return;
            }

            // Метаданные и ссылка на новую копию публикуются после записи пар.
            // При ошибке объект родительского редактора сохраняет прежний источник.
            var updated = new Timeline
            {
                Id = _timeline.Id, Name = _timeline.Name,
                NotificationsEnabled = _timeline.NotificationsEnabled, BaseDays = parsed.BaseDays,
                Source = new ImportSource
                {
                    FileName = _sourceFileName, StoredFileName = Path.GetFileName(_filePath),
                    GroupName = group.FullGroupName, ImportedAt = DateTime.Now
                }
            };

            if (!_timelineSaved)
            {
                // Имя, введенное пользователем, приоритетнее автоматического
                if (string.IsNullOrWhiteSpace(_timeline.Name))
                    _timeline.Name = $"{group.FullGroupName} ({DateTime.Now:dd.MM.yyyy})";
                updated.Name = _timeline.Name;
                dataMayHaveChanged = true;
                await _timelineRepo.AddAsync(_timeline);
                _timelineSaved = true;
            }

            var (stored, removed) = await LessonImportService.ReplaceAllAsync(
                _lessonRepo, _timeline.Id, lessons, () => dataMayHaveChanged = true);

            var previousPath = ScheduleSourceStore.PathFor(_timeline.Id, _timeline.Source);
            dataMayHaveChanged = true;
            await _timelineRepo.UpdateAsync(updated);
            _timeline.BaseDays = updated.BaseDays;
            _timeline.Source = updated.Source;
            _sourceCommitted = true;
            if (!string.Equals(previousPath, _filePath, StringComparison.OrdinalIgnoreCase))
                ScheduleSourceStore.DiscardPending(_timeline.Id, previousPath);
            _onImported?.Invoke();
            dataMayHaveChanged = false;
            AppEvents.NotifyDataChanged();

            var report = $"Пар в расписании: {stored}.";
            if (removed > 0) report += $"\nЗаменено прежних: {removed}.";
            await ShowAlertAsync("Импорт завершён", $"{report}\nПроверьте корректность данных.");

            await SafeClosePagesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка импорта группы {Group} в {TimelineId}", group.FullGroupName, _timeline.Id);
            if (dataMayHaveChanged)
            {
                dataMayHaveChanged = false;
                AppEvents.NotifyDataChanged();
            }
            await ShowAlertAsync("Ошибка", ex is IncompleteLessonReadException ? ex.Message
                : "Не удалось импортировать расписание.");
        }
        finally
        {
            IsProcessing = false;
            if (dataMayHaveChanged) AppEvents.NotifyDataChanged();
        }
    }

    /// <summary>
    /// Спрашивает перед заменой непустого расписания. Если показать вопрос негде,
    /// импорт продолжается: пользователь нажал группу сам, и молчаливый отказ
    /// выглядел бы поломкой кнопки.
    /// </summary>
    private static async Task<bool> ConfirmReplaceAsync(string group, int existing)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return true;
        return await page.DisplayAlertAsync("Заменить расписание",
            $"В расписании пар: {existing}. Импорт группы «{group}» заменит их тем, что в файле.",
            "Заменить", "Отмена");
    }

    // Application.MainPage и Page.DisplayAlert объявлены устаревшими в MAUI 10
    private static Task ShowAlertAsync(string title, string message)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page?.DisplayAlertAsync(title, message, "OK") ?? Task.CompletedTask;
    }

    private async Task SafeClosePagesAsync()
    {
        try
        {
            // Закрываем все модальные окна
            while (Shell.Current?.Navigation.ModalStack.Count > 0)
            {
                await _navigationService.PopModalAsync();
            }
        }
        catch { }
    }

    private async Task ShowErrorAndCloseAsync(string message)
    {
        try
        {
            DiscardUnfinishedSource();
            await ShowAlertAsync("Ошибка", message);
            await _navigationService.PopModalAsync();
        }
        catch { }
    }

    /// <summary>
    /// Уход без успешного импорта: удаляется только временная копия этой попытки.
    /// Вызывается только с явных путей отмены: OnDisappearing не годится, на Android
    /// он приходит и при сворачивании приложения.
    /// </summary>
    public void DiscardUnfinishedSource()
    {
        if (IsProcessing || _sourceCommitted) return;
        ScheduleSourceStore.DiscardPending(_timeline.Id, _filePath);
    }
}
