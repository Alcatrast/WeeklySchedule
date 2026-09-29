using WeeklySchedule.Models;
using WeeklySchedule.ViewModels;
using WeeklySchedule.Views;

namespace WeeklySchedule.Services;

public interface IGroupSelectionPageFactory
{
    Task OpenAsync(string filePath, bool timelineExists, Timeline timeline, Action? onImported = null);
}
public class GroupSelectionPageFactory(IServiceProvider serviceProvider, INavigationService navigationService) : IGroupSelectionPageFactory
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly INavigationService _navigationService = navigationService;
    public async Task OpenAsync(string filePath, bool timelineExists, Timeline timeline, Action? onImported = null)
    {
        var page = new GroupSelectionPage();
        var vm = _serviceProvider.GetRequiredService<GroupSelectionViewModel>();
        vm.Initialize(filePath, timelineExists, timeline, onImported);
        page.BindingContext = vm;
        await _navigationService.PushModalAsync(page);
    }
}