namespace WeeklySchedule.Services;

public interface INavigationService
{
    Task PushModalAsync(Page page);
    Task PopModalAsync();
    Task GoToAsync(string route);
}