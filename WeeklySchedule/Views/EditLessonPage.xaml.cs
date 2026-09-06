using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Messaging;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Views;

public partial class EditLessonPage : ContentPage
{
    private static bool _isOpen;

    /// <summary>
    /// Открыт ли редактор пары. Обработчики двойного тапа сверяются с флагом,
    /// чтобы второй тап не открыл вторую копию страницы.
    /// </summary>
    public static bool IsOpen => _isOpen;

    /// <summary>
    /// Показывает страницу как модальную. Флаг ставится здесь, а не в конструкторе:
    /// страницу можно создать и не показать (нет Shell, упал PushModalAsync), тогда
    /// OnDisappearing не придет, флаг залипнет в true и редактор перестанет
    /// открываться до перезапуска приложения.
    /// </summary>
    public static async Task OpenModalAsync(EditLessonPage page, bool wrapInNavigationPage = false)
    {
        var navigation = Shell.Current?.Navigation;
        if (navigation == null || _isOpen) return;

        _isOpen = true;
        try
        {
            await navigation.PushModalAsync(wrapInNavigationPage ? new NavigationPage(page) : page);
        }
        catch
        {
            _isOpen = false;
            throw;
        }
    }

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
                    else
                    {
                        if (_endTime <= _startTime)
                            EndTime = _startTime.Add(TimeSpan.FromMinutes(1));
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
        BindingContext = this;
        _isEditMode = lesson != null;
        _lesson = lesson ?? new Lesson { Day = preselectedDay ?? DayOfWeek.Monday };

        PageTitle.Text = _isEditMode ? "Редактирование пары" : "Новая пара";
        BorderDelete.IsVisible = _isEditMode;

        EntryName.Text = _lesson.Name;
        EditorDesc.Text = _lesson.Description;
        PickerType.SelectedItem = GetRussianTypeName(_lesson.Type);
        PickerDay.SelectedItem = GetRussianDayName(_lesson.Day);

        // Время и таймлайн приезжают из хранилища асинхронно. До этого сохранять
        // нечего: в режиме редактирования поля времени еще нули, в режиме создания
        // не выбран таймлайн, и пользователь получал ошибку, которая врет о причине
        SetButtonsEnabled(false);
        SafeFireAndForget.Run(() => LoadTimelinesAsync(activeTimelineId, preselectedTime));
    }

    private async Task LoadTimelinesAsync(Guid? activeTimelineId, TimeSpan? preselectedTime)
    {
        try
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

                _startTime = preselectedTime ?? TimeContext.Now.TimeOfDay;
                OnPropertyChanged(nameof(StartTime));
                _durationText = DefaultDurationMinutes().ToString();
                OnPropertyChanged(nameof(DurationText));
                RecalculateFromStart();
            }
        }
        finally
        {
            // Отмена должна работать даже если каталог не прочитался
            SetButtonsEnabled(true);
        }
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
        _isOpen = false;
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

            int enteredMinutes = int.TryParse(DurationText, out var m) ? m : 0;
            var maxEnd = new TimeSpan(23, 59, 0);
            var theoreticalEnd = StartTime + TimeSpan.FromMinutes(enteredMinutes);
            var endTime = EndTime;

            if (!LessonTimeRange.IsValid(StartTime, endTime))
            {
                await DisplayAlertAsync("Ошибка", "Конец пары должен быть позже начала и не позже 23:59.", "ОК");
                _isProcessing = false;
                SetButtonsEnabled(true);
                return;
            }

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
                endTime = maxEnd;
            }

            // Не меняем объект исходной карточки до подтверждения и успешной записи.
            var savedLesson = new Lesson
            {
                Id = _lesson.Id,
                Name = EntryName.Text.Trim(),
                Description = EditorDesc.Text?.Trim() ?? string.Empty,
                Type = GetLessonTypeFromRussianName(PickerType.SelectedItem?.ToString() ?? "Лекция"),
                Day = GetDayFromRussianName(PickerDay.SelectedItem?.ToString() ?? "Понедельник"),
                StartTime = StartTime,
                EndTime = endTime,
                TimelineId = selectedTimeline.Id
            };

            var repo = GetRepository();
            if (_isEditMode) await repo.UpdateAsync(savedLesson);
            else await repo.AddAsync(savedLesson);

            AppEvents.NotifyDataChanged();
            if (await SafePopModalAsync()) return;
            // Запись прошла, а закрыться не вышло: страница остается на экране,
            // и кнопки должны снова работать
            _isProcessing = false;
            SetButtonsEnabled(true);
        }
        catch (Exception ex)
        {
            // Debug.WriteLine и так вырезается в Release. Собственный #if DEBUG
            // вокруг него оставлял там переменную ex без единого использования
            System.Diagnostics.Debug.WriteLine($"Save error: {ex}");
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
            bool confirm = await ItemActions.DeleteLessonAsync(_lesson);
            if (confirm && await SafePopModalAsync()) return;
            _isProcessing = false;
            SetButtonsEnabled(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delete error: {ex}");
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
            // Не закрылись — значит страница осталась на экране, и блокировать
            // ей кнопки навсегда нельзя: выйти было бы уже нечем
            if (await SafePopModalAsync()) return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cancel error: {ex}");
        }
        _isProcessing = false;
        SetButtonsEnabled(true);
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

    /// <summary>Возвращает true, если страница действительно закрыта.</summary>
    private async Task<bool> SafePopModalAsync()
    {
        try
        {
            if (Navigation.ModalStack.Count > 0)
            {
                await Navigation.PopModalAsync();
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PopModal safe catch: {ex}");
        }
        return false;
    }
}
