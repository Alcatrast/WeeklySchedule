using WeeklySchedule.Models;
using WeeklySchedule.ViewModels;
using WeeklySchedule.Views;

namespace WeeklySchedule.Services;

public interface IGroupSelectionPageFactory
{
    Task OpenAsync(string filePath, bool timelineExists, Timeline timeline, Action? onImported = null);
}

public class GroupSelectionPageFactory : IGroupSelectionPageFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INavigationService _navigationService;

    public GroupSelectionPageFactory(IServiceProvider serviceProvider, INavigationService navigationService)
    {
        _serviceProvider = serviceProvider;
        _navigationService = navigationService;
    }

    public async Task OpenAsync(string filePath, bool timelineExists, Timeline timeline, Action? onImported = null)
    {
        var page = _serviceProvider.GetRequiredService<GroupSelectionPage>();
        var vm = _serviceProvider.GetRequiredService<GroupSelectionViewModel>();

        vm.Initialize(filePath, timelineExists, timeline, onImported);
        page.BindingContext = vm;

        await _navigationService.PushModalAsync(page);
    }
}