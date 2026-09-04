using System.Globalization;
using System.Windows.Input;
using WeeklySchedule.Core;
using WeeklySchedule.Models;
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
    public ICommand EditLessonCommand { get; }

    public DayViewModel(DateTime date)
    {
        Date = date.Date;
        EditLessonCommand = new Command<Lesson>(OnEditLesson);
    }

    private async void OnEditLesson(Lesson? lesson)
    {
        if (EditLessonPage.IsOpen) return;
        if (lesson == null) return;

        var editPage = new EditLessonPage(lesson);
        var navPage = new NavigationPage(editPage);
        if (Shell.Current != null) await Shell.Current.Navigation.PushModalAsync(navPage);
    }

    public void RequestScroll() => ScrollToCurrentRequested?.Invoke();

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
        Layout = TimelineLayoutBuilder.Build(Date, allLessons, now);
        LayoutUpdated?.Invoke();
    }
}