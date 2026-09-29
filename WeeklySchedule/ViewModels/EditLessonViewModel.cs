using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Messaging;
using WeeklySchedule.Models;
using WeeklySchedule.Resources.Strings;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.ViewModels;

public partial class EditLessonViewModel : BaseViewModel
{
    private readonly ITimelineRepository _timelineRepo;
    private readonly ILessonRepository _lessonRepo;
    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;

    private Lesson _lesson = null!;
    private bool _isEditMode;
    private bool _isDurationLastEdited = true;
    private bool _isUpdatingTime;
    private bool _isProcessing;
    [ObservableProperty] public partial string Title { get; set; } = AppResources.NewLesson;
    [ObservableProperty] public partial bool IsDeleteVisible { get; set; }
    [ObservableProperty] public partial TimeSpan StartTime { get; set; }
    [ObservableProperty] public partial TimeSpan EndTime { get; set; }
    [ObservableProperty] public partial string DurationText { get; set; } = "85";
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Description { get; set; } = string.Empty;
    [ObservableProperty] public partial int SelectedTypeIndex { get; set; }
    [ObservableProperty] public partial int SelectedDayIndex { get; set; }
    [ObservableProperty] public partial Timeline? SelectedTimeline { get; set; }
    public ObservableCollection<Timeline> Timelines { get; } = [];
    public List<string> LessonTypes { get; } = [];
    public List<string> DaysOfWeek { get; } = [];

    public EditLessonViewModel(
        ITimelineRepository timelineRepo,
        ILessonRepository lessonRepo,
        ISettingsService settingsService,
        INavigationService navigationService)
    {
        _timelineRepo = timelineRepo;
        _lessonRepo = lessonRepo;
        _settingsService = settingsService;
        _navigationService = navigationService;

        InitializePickers();
    }

    private void InitializePickers()
    {
        LessonTypes.AddRange([AppResources.Lecture, AppResources.Seminar, AppResources.Practice, AppResources.Lab]);

        var culture = CultureInfo.CurrentCulture;
        foreach (var day in Enum.GetValues<DayOfWeek>())
            DaysOfWeek.Add(culture.DateTimeFormat.GetDayName(day));
    }

    public async Task InitializeAsync(Lesson? lesson, DayOfWeek? preselectedDay, TimeSpan? preselectedTime, Guid? activeTimelineId)
    {
        _isEditMode = lesson != null;
        _lesson = lesson ?? new Lesson { Day = preselectedDay ?? DayOfWeek.Monday };

        Title = _isEditMode ? AppResources.EditLesson : AppResources.NewLesson;
        IsDeleteVisible = _isEditMode;

        Name = _lesson.Name;
        Description = _lesson.Description;
        SelectedTypeIndex = (int)_lesson.Type;
        SelectedDayIndex = (int)_lesson.Day;

        var timelines = (await _timelineRepo.GetAllAsync()).ToList();
        Timelines.Clear();
        foreach (var t in timelines) Timelines.Add(t);

        if (_isEditMode)
        {
            SelectedTimeline = timelines.FirstOrDefault(t => t.Id == _lesson.TimelineId);
            StartTime = _lesson.StartTime;
            EndTime = _lesson.EndTime;
            int durationMinutes = (int)(EndTime - StartTime).TotalMinutes;
            DurationText = (durationMinutes <= 0 ? 1 : durationMinutes).ToString();
            _isDurationLastEdited = false;
        }
        else
        {
            var defaultId = activeTimelineId ?? Guid.Empty;
            SelectedTimeline = timelines.FirstOrDefault(t => t.Id == defaultId) ?? timelines.FirstOrDefault();
            _isDurationLastEdited = true;
            StartTime = preselectedTime ?? TimeContext.Now.TimeOfDay;
            DurationText = DefaultDurationMinutes().ToString();
            RecalculateFromStart();
        }
    }

    private int DefaultDurationMinutes()
    {
        var minutes = _settingsService.DefaultLessonDuration;
        return minutes > 0 ? minutes : 85;
    }

    #region Time Recalculation Logic
    partial void OnStartTimeChanged(TimeSpan value)
    {
        if (_isUpdatingTime) return;
        if (_isDurationLastEdited) RecalculateFromStart();
        else
        {
            if (EndTime <= StartTime) EndTime = StartTime.Add(TimeSpan.FromMinutes(1));
            RecalculateDuration();
        }
    }

    partial void OnEndTimeChanged(TimeSpan value)
    {
        var newValue = LessonTimeRange.NormalizeEnd(StartTime, value);
        if (EndTime != newValue)
        {
            if (!_isUpdatingTime) _isDurationLastEdited = false;
            EndTime = newValue;
            if (!_isUpdatingTime) RecalculateDuration();
        }
    }

    partial void OnDurationTextChanged(string value)
    {
        if (_isUpdatingTime) return;
        if (!int.TryParse(value, out int minutes) || minutes <= 0)
        {
            _isUpdatingTime = true;
            DurationText = "1";
            _isUpdatingTime = false;
        }
        else
        {
            _isDurationLastEdited = true;
            RecalculateFromStart();
        }
    }

    private void RecalculateFromStart()
    {
        int minutes = int.TryParse(DurationText, out var m) ? m : 0;
        if (minutes <= 0) minutes = 1;
        var newEnd = StartTime + TimeSpan.FromMinutes(minutes);
        var maxEnd = new TimeSpan(23, 59, 0);

        _isUpdatingTime = true;
        EndTime = newEnd > maxEnd ? maxEnd : newEnd;
        _isUpdatingTime = false;
    }

    private void RecalculateDuration()
    {
        var duration = EndTime - StartTime;
        int minutes = (int)duration.TotalMinutes;
        if (minutes <= 0) minutes = 1;

        _isUpdatingTime = true;
        DurationText = minutes.ToString();
        _isUpdatingTime = false;
    }
    #endregion

    #region Commands
    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task SaveAsync()
    {
        _isProcessing = true;
        SaveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();

        try
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                await ShowAlertAsync(AppResources.Error, AppResources.EnterLessonName);
                return;
            }
            if (SelectedTimeline is null)
            {
                await ShowAlertAsync(AppResources.Error, AppResources.SelectTimelineError);
                return;
            }

            int enteredMinutes = int.TryParse(DurationText, out var m) ? m : 0;
            var maxEnd = new TimeSpan(23, 59, 0);
            var theoreticalEnd = StartTime + TimeSpan.FromMinutes(enteredMinutes);
            var endTime = EndTime;

            if (!LessonTimeRange.IsValid(StartTime, endTime))
            {
                await ShowAlertAsync(AppResources.Error, AppResources.InvalidTimeError);
                return;
            }

            if (_isDurationLastEdited && theoreticalEnd > maxEnd)
            {
                bool confirm = await ShowConfirmAsync(
                    AppResources.TimeExceededTitle,
                    string.Format(AppResources.TimeExceededMsg, enteredMinutes));
                if (!confirm) return;
                endTime = maxEnd;
            }

            var savedLesson = new Lesson
            {
                Id = _lesson.Id,
                Name = Name.Trim(),
                Description = Description?.Trim() ?? string.Empty,
                Type = (LessonType)SelectedTypeIndex,
                Day = (DayOfWeek)SelectedDayIndex,
                StartTime = StartTime,
                EndTime = endTime,
                TimelineId = SelectedTimeline.Id
            };

            if (_isEditMode) await _lessonRepo.UpdateAsync(savedLesson);
            else await _lessonRepo.AddAsync(savedLesson);

            WeakReferenceMessenger.Default.Send(new DataChangedMessage(null));
            await _navigationService.PopModalAsync();
        }
        catch (Exception ex)
        {
            await ShowAlertAsync(AppResources.Error, string.Format(AppResources.SaveError, ex.Message));
        }
        finally
        {
            _isProcessing = false;
            SaveCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanProcess() => !_isProcessing;

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task DeleteAsync()
    {
        _isProcessing = true;
        DeleteCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();

        try
        {
            bool confirm = await ShowConfirmAsync(AppResources.DeleteLesson, AppResources.ConfirmDelete, AppResources.DeleteConfirmBtn, AppResources.Cancel);
            if (confirm)
            {
                var day = _lesson.Day;
                await _lessonRepo.DeleteAsync(_lesson.Id);
                WeakReferenceMessenger.Default.Send(new DataChangedMessage(day));
                await _navigationService.PopModalAsync();
            }
        }
        catch (Exception ex)
        {
            await ShowAlertAsync(AppResources.Error, string.Format(AppResources.DeleteError, ex.Message));
        }
        finally
        {
            _isProcessing = false;
            DeleteCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await _navigationService.PopModalAsync();
    }
    #endregion

    #region UI Helpers
    private static Task ShowAlertAsync(string title, string message)
    {
        var page = Application.Current?.Windows[0]?.Page;
        return page?.DisplayAlertAsync(title, message, AppResources.OK) ?? Task.CompletedTask;
    }

    private static Task<bool> ShowConfirmAsync(string title, string message, string accept = "Yes", string cancel = "Cancel")
    {
        var page = Application.Current?.Windows[0]?.Page;
        return page?.DisplayAlertAsync(title, message, accept, cancel) ?? Task.FromResult(false);
    }
    #endregion
}