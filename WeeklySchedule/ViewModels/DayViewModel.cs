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
    private LessonState[]? _snapshot;
    private sealed record LessonState(Guid Id, Guid TimelineId, string Name, string Description,
        TimeSpan Start, TimeSpan End, LessonType Type);
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

    public void UpdateLayout(DateTime now, List<Lesson> allLessons)
    {
        var snapshot = allLessons.Where(l => l.Day == DayOfWeek).OrderBy(l => l.Id)
            .Select(l => new LessonState(l.Id, l.TimelineId, l.Name, l.Description, l.StartTime, l.EndTime, l.Type)).ToArray();
        if (_snapshot == null || !_snapshot.SequenceEqual(snapshot))
        {
            Layout = TimelineLayoutBuilder.Build(Date, allLessons, now);
            _snapshot = snapshot;
        }
        else TimelineLayoutBuilder.RefreshState(Layout, Date, now);
        LayoutUpdated?.Invoke();
    }
}
