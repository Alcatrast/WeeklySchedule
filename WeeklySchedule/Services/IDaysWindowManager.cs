using WeeklySchedule.Models;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Services;

public interface IDaysWindowManager
{
    IReadOnlyList<DayViewModel> Days { get; }
    void Initialize(DateTime today);
    void RollWindow(DateTime today);
    void UpdateAllTitles(DateTime now);
    void UpdateAllLayouts(DateTime now, List<Lesson> allLessons);
}