using WeeklySchedule.Models;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Services;

public class DaysWindowManager(
        IActiveScheduleService scheduleService,
        IEditLessonPageFactory editLessonPageFactory) : IDaysWindowManager
{
    private readonly List<DayViewModel> _days = [];
    private readonly IActiveScheduleService _scheduleService = scheduleService;

    private readonly IEditLessonPageFactory _editLessonPageFactory = editLessonPageFactory;


    public IReadOnlyList<DayViewModel> Days => _days;


    public void Initialize(DateTime today)
    {
        _days.Clear();
        for (int i = 0; i < 7; i++)
        {
            var day = new DayViewModel(today.AddDays(i), _scheduleService, _editLessonPageFactory);
            day.UpdateTitle(today);
            _days.Add(day);
        }
    }

    public void RollWindow(DateTime today)
    {
        while (_days.Count > 0 && _days[0].Date < today)
            _days.RemoveAt(0);

        if (_days.Count == 0)
        {
            Initialize(today);
            return;
        }

        while (_days.Count < 7)
            _days.Add(new DayViewModel(_days[^1].Date.AddDays(1), _scheduleService, _editLessonPageFactory));
    }

    public void UpdateAllTitles(DateTime now)
    {
        foreach (var dayVM in _days) dayVM.UpdateTitle(now);
    }

    public void UpdateAllLayouts(DateTime now, List<Lesson> allLessons)
    {
        foreach (var dayVM in _days) dayVM.UpdateLayout(now, allLessons);
    }
}