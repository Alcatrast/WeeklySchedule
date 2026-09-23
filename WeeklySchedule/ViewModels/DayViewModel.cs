using System.Globalization;
using System.Windows.Input;
using WeeklySchedule.Core;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Resources.Strings;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.ViewModels;

public partial class DayViewModel : BaseViewModel
{
    private readonly IEditLessonPageFactory _pageFactory;
    private readonly IActiveScheduleService _scheduleService;

    public DateTime Date { get; }
    public DayOfWeek DayOfWeek => Date.DayOfWeek;

    private string _dayTitle = string.Empty;
    public string DayTitle { get => _dayTitle; set => SetProperty(ref _dayTitle, value); }

    public TimelineLayout Layout { get; private set; } = new();

    public event Action? LayoutUpdated;
    public event Action? ScrollToCurrentRequested;

    public ICommand EditLessonCommand { get; }
    public ICommand CreateLessonCommand { get; }

    public DayViewModel(DateTime date, IActiveScheduleService scheduleService, IEditLessonPageFactory pageFactory)
    {
        Date = date.Date;
        _scheduleService = scheduleService;
        _pageFactory = pageFactory;
        EditLessonCommand = new Command<Lesson>(OnEditLesson);
        CreateLessonCommand = new Command(OnCreateLesson);
    }

    private void OnEditLesson(Lesson? lesson) { if (_pageFactory.IsOpen || lesson == null) return; SafeFireAndForget.Run(() => _pageFactory.OpenAsync(lesson, activeTimelineId: _scheduleService.ActiveTimelineId)); }
    private void OnCreateLesson() { if (_pageFactory.IsOpen) return; SafeFireAndForget.Run(() => _pageFactory.OpenAsync(preselectedDay: DayOfWeek, activeTimelineId: _scheduleService.ActiveTimelineId)); }

    public void RequestScroll() => ScrollToCurrentRequested?.Invoke();

    public void UpdateTitle(DateTime now)
    {
        int diff = (Date - now.Date).Days;
        string prefix = diff switch
        {
            0 => AppResources.Today,
            1 => AppResources.Tomorrow,
            2 => AppResources.DayAfterTomorrow,
            _ => ""
        };

        var culture = CultureInfo.CurrentCulture;
        string dayOfWeekStr = Date.ToString("dddd", culture);
        if (!string.IsNullOrEmpty(dayOfWeekStr))
            dayOfWeekStr = char.ToUpper(dayOfWeekStr[0]) + dayOfWeekStr[1..];

        string dateStr = Date.ToString("d", culture);

        DayTitle = string.IsNullOrEmpty(prefix)
            ? $"{dayOfWeekStr}, {dateStr}"
            : $"{dayOfWeekStr}, {prefix}, {dateStr}";
    }

    public void UpdateLayout(DateTime now, List<Lesson> allLessons) { Layout = TimelineLayoutBuilder.Build(Date, allLessons, now); LayoutUpdated?.Invoke(); }
}