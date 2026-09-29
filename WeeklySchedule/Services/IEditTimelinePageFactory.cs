using WeeklySchedule.Models;
using WeeklySchedule.Views;

namespace WeeklySchedule.Services;

public interface IEditTimelinePageFactory
{
    Task OpenAsync(Timeline? timeline = null);
}

public class EditTimelinePageFactory : IEditTimelinePageFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INavigationService _navigationService;

    public EditTimelinePageFactory(IServiceProvider serviceProvider, INavigationService navigationService)
    {
        _serviceProvider = serviceProvider;
        _navigationService = navigationService;
    }

    public async Task OpenAsync(Timeline? timeline = null)
    {
        var page = _serviceProvider.GetRequiredService<EditTimelinePage>();
        page.Initialize(timeline);
        await _navigationService.PushModalAsync(page);
    }
}