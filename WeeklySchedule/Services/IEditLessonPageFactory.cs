using WeeklySchedule.Models;
using WeeklySchedule.Views;

namespace WeeklySchedule.Services;

public interface IEditLessonPageFactory
{
    bool IsOpen { get; }
    Task OpenAsync(Lesson? lesson = null, DayOfWeek? preselectedDay = null, TimeSpan? preselectedTime = null, Guid? activeTimelineId = null);
    void NotifyClosed();
}

public class EditLessonPageFactory(IServiceProvider serviceProvider) : IEditLessonPageFactory
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private bool _isOpen;

    public bool IsOpen => _isOpen;

    public async Task OpenAsync(Lesson? lesson = null, DayOfWeek? preselectedDay = null, TimeSpan? preselectedTime = null, Guid? activeTimelineId = null)
    {
        if (_isOpen) return;
        _isOpen = true;

        try
        {
            var page = _serviceProvider.GetRequiredService<EditLessonPage>();
            page.Initialize(lesson, preselectedDay, preselectedTime, activeTimelineId);
            var nav = _serviceProvider.GetRequiredService<INavigationService>();
            await nav.PushModalAsync(page);
        }
        catch
        {
            _isOpen = false;
            throw;
        }
    }

    public void NotifyClosed() => _isOpen = false;
}