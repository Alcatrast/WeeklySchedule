using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Messaging;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Views;

public partial class EditLessonPage : ContentPage
{
    public static bool IsOpen { get; private set; } = false;
    private bool _isProcessing = false;
    private readonly Lesson _lesson;
    private readonly bool _isEditMode;
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
                    else RecalculateDuration();
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
            var newValue = value;
            var maxEnd = new TimeSpan(23, 59, 0);
            if (newValue > maxEnd) newValue = maxEnd;
            if (newValue <= _startTime)
            {
                newValue = _startTime.Add(TimeSpan.FromMinutes(1));
                if (newValue > maxEnd) newValue = maxEnd;
            }
            if (_endTime != newValue)
            {
                if (!_isUpdatingTime) _isDurationLastEdited = false;
                _endTime = newValue;
                OnPropertyChanged();
                if (!_isUpdatingTime) RecalculateDuration();
            }
        }
    }

    // Длительность новой пары берется из настроек, см. DefaultDurationMinutes
    private string _durationText = "85";
    public string DurationText
    {
        get => _durationText;
        set
        {
            if (!int.TryParse(value, out int minutes) || minutes <= 0)
            {
                value = "1";
            }
            if (_durationText != value)
            {
                if (!_isUpdatingTime) _isDurationLastEdited = true;
                _durationText = value;
                OnPropertyChanged();
                if (!_isUpdatingTime) RecalculateFromStart();
            }
        }
    }

    public EditLessonPage(Lesson? lesson = null, DayOfWeek? preselectedDay = null, TimeSpan? preselectedTime = null, Guid? activeTimelineId = null)
    {
        InitializeComponent();
        IsOpen = true;
        BindingContext = this;
        _isEditMode = lesson != null;
        _lesson = lesson ?? new Lesson { Day = preselectedDay ?? DayOfWeek.Monday };

        PageTitle.Text = _isEditMode ? "Редактирование пары" : "Новая пара";
        BorderDelete.IsVisible = _isEditMode;

        EntryName.Text = _lesson.Name;
        EditorDesc.Text = _lesson.Description;
        PickerType.SelectedItem = GetRussianTypeName(_lesson.Type);
        PickerDay.SelectedItem = GetRussianDayName(_lesson.Day);

        // ИСПРАВЛЕНО: Передаем preselectedTime в асинхронный метод
        _ = LoadTimelinesAsync(activeTimelineId, preselectedTime);
    }

    // ИСПРАВЛЕНО: Добавлен параметр TimeSpan? preselectedTime в сигнатуру
    private async Task LoadTimelinesAsync(Guid? activeTimelineId, TimeSpan? preselectedTime)
    {
        var timelineRepo = Application.Current!.Handler!.MauiContext!.Services.GetRequiredService<ITimelineRepository>();
        var timelines = (await timelineRepo.GetAllAsync()).ToList();

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

            // ТЕПЕРЬ preselectedTime доступна в этой области видимости
            _startTime = preselectedTime ?? TimeContext.Now.TimeOfDay;
            OnPropertyChanged(nameof(StartTime));
            _durationText = DefaultDurationMinutes().ToString();
            OnPropertyChanged(nameof(DurationText));
            RecalculateFromStart();
        }
        _isUpdatingTime = false;
    }

    // Настройка "Длительность пары по умолчанию" до этого никем не читалась
    private static int DefaultDurationMinutes()
    {
        var settings = Application.Current!.Handler!.MauiContext!.Services.GetRequiredService<ISettingsService>();
        var minutes = settings.DefaultLessonDuration;
        return minutes > 0 ? minutes : 85;
    }

    private ILessonRepository GetRepository()
    {
        return Application.Current!.Handler!.MauiContext!.Services.GetRequiredService<ILessonRepository>();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        IsOpen = false;
        _isProcessing = false;
    }

    private static string GetRussianTypeName(LessonType type) => type switch
    {
        LessonType.Lecture => "Лекция",
        LessonType.Seminar => "Семинар",
        LessonType.Practice => "Практика",
        LessonType.Lab => "Лабораторная",
        _ => "Лекция"
    };

    private static LessonType GetLessonTypeFromRussianName(string name) => name switch
    {
        "Лекция" => LessonType.Lecture,
        "Семинар" => LessonType.Seminar,
        "Практика" => LessonType.Practice,
        "Лабораторная" => LessonType.Lab,
        _ => LessonType.Lecture
    };

    private static string GetRussianDayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Понедельник",
        DayOfWeek.Tuesday => "Вторник",
        DayOfWeek.Wednesday => "Среда",
        DayOfWeek.Thursday => "Четверг",
        DayOfWeek.Friday => "Пятница",
        DayOfWeek.Saturday => "Суббота",
        DayOfWeek.Sunday => "Воскресенье",
        _ => "Понедельник"
    };

    private static DayOfWeek GetDayFromRussianName(string name) => name switch
    {
        "Понедельник" => DayOfWeek.Monday,
        "Вторник" => DayOfWeek.Tuesday,
        "Среда" => DayOfWeek.Wednesday,
        "Четверг" => DayOfWeek.Thursday,
        "Пятница" => DayOfWeek.Friday,
        "Суббота" => DayOfWeek.Saturday,
        "Воскресенье" => DayOfWeek.Sunday,
        _ => DayOfWeek.Monday
    };

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
        else
        {
            RecalculateFromStart();
        }
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
                await DisplayAlertAsync("Ошибка", "Введите название пары", "ОК");
                _isProcessing = false;
                SetButtonsEnabled(true);
                return;
            }
            if (PickerTimeline.SelectedItem is not Timeline selectedTimeline)
            {
                await DisplayAlertAsync("Ошибка", "Пожалуйста, выберите таймлайн", "ОК");
                _isProcessing = false;
                SetButtonsEnabled(true);
                return;
            }

            _lesson.Name = EntryName.Text.Trim();
            // Editor.Text равен null, пока пользователь ничего не ввел
            _lesson.Description = EditorDesc.Text?.Trim() ?? string.Empty;
            _lesson.Type = GetLessonTypeFromRussianName(PickerType.SelectedItem?.ToString() ?? "Лекция");
            _lesson.Day = GetDayFromRussianName(PickerDay.SelectedItem?.ToString() ?? "Понедельник");
            _lesson.StartTime = StartTime;
            _lesson.TimelineId = selectedTimeline.Id;

            int enteredMinutes = int.TryParse(DurationText, out var m) ? m : 0;
            var maxEnd = new TimeSpan(23, 59, 0);
            var theoreticalEnd = StartTime + TimeSpan.FromMinutes(enteredMinutes);

            if (_isDurationLastEdited && theoreticalEnd > maxEnd)
            {
                bool confirm = await DisplayAlertAsync(
                    "Превышение времени",
                    $"Введенная длительность ({enteredMinutes} мин.) превышает допустимый предел. Время конца пары будет автоматически установлено в 23:59. Продолжить сохранение?",
                    "Да", "Отмена");
                if (!confirm)
                {
                    _isProcessing = false;
                    SetButtonsEnabled(true);
                    return;
                }
                _lesson.EndTime = maxEnd;
            }
            else
            {
                _lesson.EndTime = EndTime;
            }

            var repo = GetRepository();
            if (_isEditMode) await repo.UpdateAsync(_lesson);
            else await repo.AddAsync(_lesson);

            AppEvents.NotifyDataChanged(_lesson.Day);
            await SafePopModalAsync();
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"Save error: {ex.Message}");
#endif
            _isProcessing = false;
            SetButtonsEnabled(true);
        }
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_isProcessing) return;
        _isProcessing = true;
        SetButtonsEnabled(false);
        try
        {
            bool confirm = await DisplayAlertAsync("Подтверждение", "Вы уверены, что хотите удалить эту пару?", "Да, удалить", "Отмена");
            if (confirm)
            {
                var day = _lesson.Day;
                var repo = GetRepository();
                await repo.DeleteAsync(_lesson.Id);

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
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"Delete error: {ex.Message}");
#endif
            _isProcessing = false;
            SetButtonsEnabled(true);
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
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"Cancel error: {ex.Message}");
#endif
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
            {
                await Navigation.PopModalAsync();
            }
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"PopModal safe catch: {ex.Message}");
#endif
        }
    }
}