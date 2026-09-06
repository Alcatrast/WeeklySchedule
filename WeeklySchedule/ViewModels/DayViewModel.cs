using System.Globalization;
using System.Windows.Input;
using WeeklySchedule.Core;
using WeeklySchedule.Models;
using WeeklySchedule.Utilities;
using WeeklySchedule.Views;

namespace WeeklySchedule.ViewModels;

public partial class DayViewModel : BaseViewModel
{
    public DateTime Date { get; }
    public DayOfWeek DayOfWeek => Date.DayOfWeek;

    private string _dayTitle = string.Empty;
    public string DayTitle
    {
        get => _dayTitle;
        set => SetProperty(ref _dayTitle, value);
    }

    public TimelineLayout Layout { get; private set; } = new();
    public event Action? LayoutUpdated;
    public event Action? ScrollToCurrentRequested;
    public ICommand ViewLessonCommand { get; }
    public ICommand LessonActionsCommand { get; }

    public DayViewModel(DateTime date)
    {
        Date = date.Date;
        ViewLessonCommand = new Command<Lesson>(lesson =>
        {
            if (lesson != null) SafeFireAndForget.Run(() => LessonDetailsPage.OpenAsync(lesson.Id));
        });
        LessonActionsCommand = new Command<Lesson>(lesson =>
        {
            if (lesson != null) SafeFireAndForget.Run(() => Services.ItemActions.ShowLessonAsync(lesson));
        });
    }

    public bool ScrollRequested { get; private set; }
    public void RequestScroll()
    {
        ScrollRequested = true;
        ScrollToCurrentRequested?.Invoke();
    }
    public void AcknowledgeScroll() => ScrollRequested = false;

    public void UpdateTitle(DateTime now)
    {
        int diff = (Date - now.Date).Days;
        string prefix = diff switch
        {
            0 => "Сегодня",
            1 => "Завтра",
            2 => "Послезавтра",
            _ => ""
        };
        string dayOfWeekRu = Date.ToString("dddd", new CultureInfo("ru-RU"));
        if (!string.IsNullOrEmpty(dayOfWeekRu))
            dayOfWeekRu = char.ToUpper(dayOfWeekRu[0]) + dayOfWeekRu[1..];

        string dateStr = Date.ToString("dd.MM.yyyy");
        DayTitle = string.IsNullOrEmpty(prefix)
            ? $"{dayOfWeekRu}, {dateStr}"
            : $"{dayOfWeekRu}, {prefix}, {dateStr}";
    }

    // Раскладку держит WeekLayout: он общий для всех дней и пересобирается только
    // при смене данных, поэтому здесь достаточно взять свой день и обновить состояние.
    // Сравнение снимка пар переехало в MainViewModel — оно делается раз на неделю,
    // а не семь раз подряд.
    public void UpdateLayout(DateTime now, WeekLayout week)
    {
        Layout = week.For(DayOfWeek);
        TimelineLayoutBuilder.RefreshState(Layout, Date, now);
        LayoutUpdated?.Invoke();
    }
}
