using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class EditLessonPage : ContentPage
{
    private readonly EditLessonViewModel _vm;

    public EditLessonPage(EditLessonViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    public async Task InitializeAsync(Lesson? lesson, DayOfWeek? preselectedDay, TimeSpan? preselectedTime, Guid? activeTimelineId)
    {
        await _vm.InitializeAsync(lesson, preselectedDay, preselectedTime, activeTimelineId);
    }
}