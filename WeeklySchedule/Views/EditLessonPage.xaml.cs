using System.Globalization;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Messaging;
using WeeklySchedule.Models;
using WeeklySchedule.Resources.Strings;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Views;

public partial class EditLessonPage : ContentPage
{
    private readonly ITimelineRepository _timelineRepo;
    private readonly ILessonRepository _lessonRepo;
    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;
    private readonly IEditLessonPageFactory _factory;
    private bool _isProcessing = false;
    private Lesson _lesson = null!;
    private bool _isEditMode;
    private bool _isDurationLastEdited = true;
    private bool _isUpdatingTime;
    private TimeSpan _startTime;

    public TimeSpan StartTime
    {
        get => _startTime;
        set
        {
            if (_startTime != value)
            {
                _startTime = value;
                OnPropertyChanged();
                if (!_isUpdatingTime)
                {
                    if (_isDurationLastEdited) RecalculateFromStart();
                    else
                    {
                        if (_endTime <= _startTime) EndTime = _startTime.Add(TimeSpan.FromMinutes(1));
                        RecalculateDuration();
                    }
                }
            }
        }
    }

    private TimeSpan _endTime;
    public TimeSpan EndTime
    {
        get => _endTime;
        set
        {
            var newValue = LessonTimeRange.NormalizeEnd(_startTime, value);
            if (_endTime != newValue)
            {
                if (!_isUpdatingTime) _isDurationLastEdited = false;
                _endTime = newValue;
                OnPropertyChanged();
                if (!_isUpdatingTime) RecalculateDuration();
            }
        }
    }

    private string _durationText = "85";
    public string DurationText
    {
        get => _durationText;
        set
        {
            if (!int.TryParse(value, out int minutes) || minutes <= 0) value = "1";
            if (_durationText != value)
            {
                if (!_isUpdatingTime) _isDurationLastEdited = true;
                _durationText = value;
                OnPropertyChanged();
                if (!_isUpdatingTime) RecalculateFromStart();
            }
        }
    }

    public EditLessonPage(ITimelineRepository timelineRepo, ILessonRepository lessonRepo, ISettingsService settingsService, INavigationService navigationService, IEditLessonPageFactory factory)
    {
        InitializeComponent();
        _timelineRepo = timelineRepo;
        _lessonRepo = lessonRepo;
        _settingsService = settingsService;
        _navigationService = navigationService;
        _factory = factory;
        BindingContext = this;

        InitializePickers();
    }

    private void InitializePickers()
    {
        // Локализованные типы занятий
        PickerType.ItemsSource = new[]
        {
            AppResources.Lecture,
            AppResources.Seminar,
            AppResources.Practice,
            AppResources.Lab
        };

        // Локализованные дни недели. 
        // Порядок Enum.GetValues<DayOfWeek>() фиксирован: Sunday=0, Monday=1 ... Saturday=6
        var culture = CultureInfo.CurrentCulture;
        PickerDay.ItemsSource = Enum.GetValues<DayOfWeek>()
            .Select(d => culture.DateTimeFormat.GetDayName(d))
            .ToList();
    }

    public void Initialize(Lesson? lesson, DayOfWeek? preselectedDay, TimeSpan? preselectedTime, Guid? activeTimelineId)
    {
        _isEditMode = lesson != null;
        _lesson = lesson ?? new Lesson { Day = preselectedDay ?? DayOfWeek.Monday };

        PageTitle.Text = _isEditMode ? AppResources.EditLesson : AppResources.NewLesson;
        BorderDelete.IsVisible = _isEditMode;

        EntryName.Text = _lesson.Name;
        EditorDesc.Text = _lesson.Description;

        // Используем индексы, так как ItemsSource генерируется в том же порядке
        PickerType.SelectedIndex = (int)_lesson.Type;
        PickerDay.SelectedIndex = (int)_lesson.Day;

        SafeFireAndForget.Run(() => LoadTimelinesAsync(activeTimelineId, preselectedTime));
    }

    private async Task LoadTimelinesAsync(Guid? activeTimelineId, TimeSpan? preselectedTime)
    {
        var timelines = (await _timelineRepo.GetAllAsync()).ToList();
        PickerTimeline.ItemsSource = timelines;
        PickerTimeline.ItemDisplayBinding = new Binding("Name");

        if (_isEditMode)
        {
            PickerTimeline.SelectedItem = timelines.FirstOrDefault(t => t.Id == _lesson.TimelineId);
            _startTime = _lesson.StartTime;
            _endTime = _lesson.EndTime;
            int durationMinutes = (int)(_endTime - _startTime).TotalMinutes;
            if (durationMinutes <= 0) durationMinutes = 1;
            _durationText = durationMinutes.ToString();
            OnPropertyChanged(nameof(StartTime));
            OnPropertyChanged(nameof(EndTime));
            OnPropertyChanged(nameof(DurationText));
            _isDurationLastEdited = false;
        }
        else
        {
            var defaultId = activeTimelineId ?? Guid.Empty;
            PickerTimeline.SelectedItem = timelines.FirstOrDefault(t => t.Id == defaultId) ?? timelines.FirstOrDefault();
            _isDurationLastEdited = true;
            _startTime = preselectedTime ?? TimeContext.Now.TimeOfDay;
            OnPropertyChanged(nameof(StartTime));
            _durationText = DefaultDurationMinutes().ToString();
            OnPropertyChanged(nameof(DurationText));
            RecalculateFromStart();
        }
        _isUpdatingTime = false;
    }

    private int DefaultDurationMinutes()
    {
        var minutes = _settingsService.DefaultLessonDuration;
        return minutes > 0 ? minutes : 85;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _factory.NotifyClosed();
        _isProcessing = false;
    }

    private void RecalculateFromStart()
    {
        int minutes = int.TryParse(DurationText, out var m) ? m : 0;
        if (minutes <= 0) minutes = 1;
        var newEnd = StartTime + TimeSpan.FromMinutes(minutes);
        var maxEnd = new TimeSpan(23, 59, 0);
        _isUpdatingTime = true;
        _endTime = newEnd > maxEnd ? maxEnd : newEnd;
        OnPropertyChanged(nameof(EndTime));
        _isUpdatingTime = false;
    }

    private void RecalculateDuration()
    {
        var duration = EndTime - StartTime;
        int minutes = (int)duration.TotalMinutes;
        if (minutes <= 0) minutes = 1;
        _isUpdatingTime = true;
        _durationText = minutes.ToString();
        OnPropertyChanged(nameof(DurationText));
        _isUpdatingTime = false;
    }

    private void OnDurationTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingTime) return;
        if (string.IsNullOrWhiteSpace(e.NewTextValue)) return;
        if (!int.TryParse(e.NewTextValue, out int minutes) || minutes <= 0)
        {
            _isUpdatingTime = true;
            DurationText = "1";
            if (sender is Entry entry) entry.Text = "1";
            _isUpdatingTime = false;
        }
        else RecalculateFromStart();
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_isProcessing) return;
        _isProcessing = true;
        SetButtonsEnabled(false);
        try
        {
            if (string.IsNullOrWhiteSpace(EntryName.Text))
            {
                await DisplayAlertAsync(AppResources.Error, AppResources.EnterLessonName, AppResources.OK);
                _isProcessing = false;
                SetButtonsEnabled(true);
                return;
            }
            if (PickerTimeline.SelectedItem is not Timeline selectedTimeline)
            {
                await DisplayAlertAsync(AppResources.Error, AppResources.SelectTimelineError, AppResources.OK);
                _isProcessing = false;
                SetButtonsEnabled(true);
                return;
            }

            int enteredMinutes = int.TryParse(DurationText, out var m) ? m : 0;
            var maxEnd = new TimeSpan(23, 59, 0);
            var theoreticalEnd = StartTime + TimeSpan.FromMinutes(enteredMinutes);
            var endTime = EndTime;

            if (!LessonTimeRange.IsValid(StartTime, endTime))
            {
                await DisplayAlertAsync(AppResources.Error, AppResources.InvalidTimeError, AppResources.OK);
                _isProcessing = false;
                SetButtonsEnabled(true);
                return;
            }

            if (_isDurationLastEdited && theoreticalEnd > maxEnd)
            {
                bool confirm = await DisplayAlertAsync(
                    AppResources.TimeExceededTitle,
                    string.Format(AppResources.TimeExceededMsg, enteredMinutes),
                    AppResources.Yes,
                    AppResources.Cancel);
                if (!confirm)
                {
                    _isProcessing = false;
                    SetButtonsEnabled(true);
                    return;
                }
                endTime = maxEnd;
            }

            var savedLesson = new Lesson
            {
                Id = _lesson.Id,
                Name = EntryName.Text.Trim(),
                Description = EditorDesc.Text?.Trim() ?? string.Empty,
                // Получаем значения по индексу, так как массив ItemsSource не меняется
                Type = (LessonType)PickerType.SelectedIndex,
                Day = (DayOfWeek)PickerDay.SelectedIndex,
                StartTime = StartTime,
                EndTime = endTime,
                TimelineId = selectedTimeline.Id
            };

            if (_isEditMode) await _lessonRepo.UpdateAsync(savedLesson);
            else await _lessonRepo.AddAsync(savedLesson);

            AppEvents.NotifyDataChanged();
            await SafePopModalAsync();
        }
        catch (Exception ex)
        {
            _isProcessing = false;
            SetButtonsEnabled(true);
            if (Application.Current?.Windows[0]?.Page is Page page)
                await page.DisplayAlertAsync(AppResources.Error, string.Format(AppResources.SaveError, ex.Message), AppResources.OK);
        }
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_isProcessing) return;
        _isProcessing = true;
        SetButtonsEnabled(false);
        try
        {
            bool confirm = await DisplayAlertAsync(
                AppResources.DeleteLesson,
                AppResources.ConfirmDelete,
                AppResources.DeleteConfirmBtn,
                AppResources.Cancel);
            if (confirm)
            {
                var day = _lesson.Day;
                await _lessonRepo.DeleteAsync(_lesson.Id);
                AppEvents.NotifyDataChanged(day);
                await SafePopModalAsync();
            }
            else
            {
                _isProcessing = false;
                SetButtonsEnabled(true);
            }
        }
        catch (Exception ex)
        {
            _isProcessing = false;
            SetButtonsEnabled(true);
            if (Application.Current?.Windows[0]?.Page is Page page)
                await page.DisplayAlertAsync(AppResources.Error, string.Format(AppResources.DeleteError, ex.Message), AppResources.OK);
        }
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (_isProcessing) return;
        _isProcessing = true;
        SetButtonsEnabled(false);
        try
        {
            await SafePopModalAsync();
        }
        catch (Exception ex)
        {
            if (Application.Current?.Windows[0]?.Page is Page page)
                await page.DisplayAlertAsync(AppResources.Error, string.Format(AppResources.CloseError, ex.Message), AppResources.OK);
        }
    }

    private void SetButtonsEnabled(bool isEnabled)
    {
        BorderSave.InputTransparent = !isEnabled;
        BorderSave.Opacity = isEnabled ? 1.0 : 0.5;
        BorderDelete.InputTransparent = !isEnabled;
        BorderDelete.Opacity = isEnabled ? 1.0 : 0.5;
        BorderCancel.InputTransparent = !isEnabled;
        BorderCancel.Opacity = isEnabled ? 1.0 : 0.5;
    }

    private async Task SafePopModalAsync()
    {
        try
        {
            if (Navigation.ModalStack.Count > 0)
                await Navigation.PopModalAsync();
        }
        catch (Exception) { }
    }
}