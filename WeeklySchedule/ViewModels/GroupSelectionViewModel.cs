using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Extensions;
using WeeklySchedule.Messaging;
using WeeklySchedule.Models;
using WeeklySchedule.Resources.Strings;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.ViewModels;
public partial class GroupSelectionViewModel : BaseViewModel
{
    private readonly ILessonRepository _lessonRepo;
    private readonly ITimelineRepository _timelineRepo;
    private readonly INavigationService _navigationService;
    private readonly ILogger<ExcelMIPTScheduleParser> _logger;
    private string _filePath = string.Empty;
    private bool _timelineExists;
    private Timeline _timeline = null!;
    private Action? _onImported;

    private GroupItem? _selectedGroup;

    [ObservableProperty]
    private bool _isLoadingGroups = true;

    [ObservableProperty]
    private bool _isProcessing;

    public ObservableCollection<GroupCategory> Categories { get; } = [];

    public GroupSelectionViewModel(
        ILessonRepository lessonRepo,
        ITimelineRepository timelineRepo,
        INavigationService navigationService,
        ILogger<ExcelMIPTScheduleParser> logger)
    {
        _lessonRepo = lessonRepo;
        _timelineRepo = timelineRepo;
        _navigationService = navigationService;
        _logger = logger;
    }
    public void Initialize(string filePath, bool timelineExists, Timeline timeline, Action? onImported)
    {
        _filePath = filePath;
        _timelineExists = timelineExists;
        _timeline = timeline;
        _onImported = onImported;
    }
    public async Task LoadDataAsync()
    {
        IsLoadingGroups = true;
        try
        {
            List<string> groups = await Task.Run(() =>
            {
                return ExcelMIPTScheduleParser.ExtractAllGroupNames(_filePath);
            });

            if (groups.Count == 0)
            {
                await ShowErrorAndCloseAsync(AppResources.NoGroupsError);
                return;
            }

            var dict = new Dictionary<string, List<GroupItem>>();
            foreach (var g in groups)
            {
                var parts = g.Split(['-'], 2);
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

                if (kvp.Key == dict.Keys.First()) Categories[^1].IsExpanded = true;
            }
        }
        catch (Exception ex)
        {
            await ShowAlertAsync(AppResources.Error, string.Format(AppResources.FileReadError, ex.Message));
            await _navigationService.PopModalAsync();
        }
        finally
        {
            IsLoadingGroups = false;
        }
    }

    #region Commands

    [RelayCommand]
    private void ToggleCategory(object? parameter)
    {
        if (parameter is not GroupCategory category) return;
        if (IsProcessing || IsLoadingGroups) return;

        foreach (var c in Categories)
            c.IsExpanded = (c == category);
    }

    [RelayCommand]
    private void SelectGroup(object? parameter)
    {
        if (parameter is not GroupItem group) return;
        if (IsProcessing || IsLoadingGroups) return;

        if (_selectedGroup == group)
        {
            SafeFireAndForget.Run(() => ImportGroupAsync(group));
        }
        else
        {
            _selectedGroup?.IsSelected = false;
            _selectedGroup = group;
            group.IsSelected = true;
        }
    }

    #endregion

    #region Private Logic

    private async Task ImportGroupAsync(GroupItem group)
    {
        IsProcessing = true;
        try
        {
            List<Lesson> lessons = await Task.Run(() =>
            {
                var parser = new ExcelMIPTScheduleParser(_logger);
                return parser.ParseGroupSchedule(_filePath, group.FullGroupName);
            });

            if (!_timelineExists)
            {
                if (string.IsNullOrWhiteSpace(_timeline.Name))
                {
                    var culture = System.Globalization.CultureInfo.CurrentCulture;
                    _timeline.Name = $"{group.FullGroupName} ({DateTime.Now.ToString("d", culture)})";
                }
            }

            foreach (var lesson in lessons)
            {
                lesson.TimelineId = _timeline.Id;
                await _lessonRepo.AddAsync(lesson);
            }

            if (_timelineExists)
                await _timelineRepo.UpdateAsync(_timeline);

            _onImported?.Invoke();

            WeakReferenceMessenger.Default.Send(new DataChangedMessage(null));

            await ShowAlertAsync(AppResources.ImportSuccessTitle, string.Format(AppResources.ImportCompleteMsg, lessons.Count));
            await SafeClosePagesAsync();
        }
        catch (Exception ex)
        {
            await ShowAlertAsync(AppResources.Error, string.Format(AppResources.ImportScheduleError, ex.Message));
        }
        finally
        {
            IsProcessing = false;
        }

        _selectedGroup?.IsSelected = false;
        _selectedGroup = null;
    }

    #endregion

    #region UI Helpers

    private static Task ShowAlertAsync(string title, string message)
    {
        var windows = Application.Current?.Windows;
        var page = (windows != null && windows.Count > 0) ? windows[0].Page : null;
        return page?.DisplayAlertAsync(title, message, AppResources.OK) ?? Task.CompletedTask;
    }

    private async Task SafeClosePagesAsync()
    {
        try
        {
            await _navigationService.PopModalAsync();
        }
        catch { }
    }

    private async Task ShowErrorAndCloseAsync(string message)
    {
        try
        {
            await ShowAlertAsync(AppResources.Error, message);
            await _navigationService.PopModalAsync();
        }
        catch { }
    }

    #endregion
}