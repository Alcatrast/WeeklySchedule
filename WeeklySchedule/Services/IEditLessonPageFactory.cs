using WeeklySchedule.Models;
using WeeklySchedule.Views;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Services;

public interface IEditLessonPageFactory
{
    bool IsOpen { get; }
    Task OpenAsync(Lesson? lesson = null, DayOfWeek? preselectedDay = null, TimeSpan? preselectedTime = null, Guid? activeTimelineId = null);
    void NotifyClosed();
}
public class EditLessonPageFactory(IServiceProvider serviceProvider, INavigationService navigationService) : IEditLessonPageFactory
{
    private bool _isOpen;

    public bool IsOpen => _isOpen;

    public async Task OpenAsync(Lesson? lesson = null, DayOfWeek? preselectedDay = null, TimeSpan? preselectedTime = null, Guid? activeTimelineId = null)
    {
        if (_isOpen) return;
        _isOpen = true;
        try
        {
            var page = serviceProvider.GetRequiredService<EditLessonPage>();
            var vm = serviceProvider.GetRequiredService<EditLessonViewModel>();

            await vm.InitializeAsync(lesson, preselectedDay, preselectedTime, activeTimelineId);

            page.BindingContext = vm;

            await navigationService.PushModalAsync(page);
        }
        catch
        {
            _isOpen = false;
            throw;
        }
    }

    public void NotifyClosed() => _isOpen = false;
}