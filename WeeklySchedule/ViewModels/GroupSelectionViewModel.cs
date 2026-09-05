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
    private readonly string _filePath;
    // Таймлайн уже сохранен в репозитории (режим "дополнить импортом")
    private readonly bool _timelineExists;
    private readonly Timeline _timeline;
    private readonly ILessonRepository _lessonRepo;
    private readonly ITimelineRepository _timelineRepo;
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider _serviceProvider;

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
        bool timelineExists,
        Timeline timeline,
        ILessonRepository lessonRepo,
        ITimelineRepository timelineRepo,
        INavigationService navigationService,
        IServiceProvider serviceProvider)
    {
        _filePath = filePath;
        _timelineExists = timelineExists;
        _timeline = timeline;
        _lessonRepo = lessonRepo;
        _timelineRepo = timelineRepo;
        _navigationService = navigationService;
        _serviceProvider = serviceProvider;

        ToggleCategoryCommand = new Command<GroupCategory>(ToggleCategory);
        SelectGroupCommand = new Command<GroupItem>(SelectGroup);
        IsLoadingGroups = true;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var groups = await Task.Run(() =>
            {
                var parser = new ExcelMIPTScheduleParser(NullLogger<ExcelMIPTScheduleParser>.Instance);
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

    private void ToggleCategory(GroupCategory category)
    {
        if (IsProcessing || IsLoadingGroups) return;
        foreach (var c in Categories) c.IsExpanded = (c == category);
    }

    private void SelectGroup(GroupItem group)
    {
        if (IsProcessing || IsLoadingGroups) return;
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
        IsProcessing = true;
        try
        {
            var lessons = await Task.Run(() =>
            {
                var parser = new ExcelMIPTScheduleParser(NullLogger<ExcelMIPTScheduleParser>.Instance);
                return parser.ParseGroupSchedule(_filePath, group.FullGroupName);
            });

            if (!_timelineExists)
            {
                // Имя, введенное пользователем, приоритетнее автоматического
                if (string.IsNullOrWhiteSpace(_timeline.Name))
                    _timeline.Name = $"{group.FullGroupName} ({DateTime.Now:dd.MM.yyyy})";
                await _timelineRepo.AddAsync(_timeline);
            }

            foreach (var lesson in lessons)
            {
                lesson.TimelineId = _timeline.Id;
                await _lessonRepo.AddAsync(lesson);
            }

            AppEvents.NotifyDataChanged();

            await ShowAlertAsync("Импорт завершён", $"Импортировано {lessons.Count} пар.\nПроверьте корректность данных.");

            await SafeClosePagesAsync();
        }
        catch (Exception)
        {
            await ShowAlertAsync("Ошибка", "Не удалось импортировать расписание.");
        }
        finally
        {
            IsProcessing = false;
        }
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
            await ShowAlertAsync("Ошибка", message);
            await _navigationService.PopModalAsync();
        }
        catch { }
    }
}