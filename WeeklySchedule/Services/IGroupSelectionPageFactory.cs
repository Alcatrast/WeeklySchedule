using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Views;

namespace WeeklySchedule.Services;

public interface IGroupSelectionPageFactory
{
    Task OpenAsync(string filePath, bool timelineExists, Timeline timeline, Action? onImported = null);
}

public class GroupSelectionPageFactory : IGroupSelectionPageFactory
{
    private readonly IServiceProvider _serviceProvider;

    public GroupSelectionPageFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task OpenAsync(string filePath, bool timelineExists, Timeline timeline, Action? onImported = null)
    {
        var page = new GroupSelectionPage(
            filePath,
            timelineExists,
            timeline,
            _serviceProvider.GetRequiredService<ILessonRepository>(),
            _serviceProvider.GetRequiredService<ITimelineRepository>(),
            _serviceProvider.GetRequiredService<INavigationService>(),
            _serviceProvider,
            onImported);

        var nav = _serviceProvider.GetRequiredService<INavigationService>();
        await nav.PushModalAsync(page);
    }
}