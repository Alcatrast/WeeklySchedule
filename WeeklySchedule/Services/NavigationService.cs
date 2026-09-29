namespace WeeklySchedule.Services;

public class NavigationService : INavigationService
{
    public async Task PushModalAsync(Page page)
    {
        if (Shell.Current is AppShell shell)
        {
            shell.FlyoutVM?.ResetEditMode();
            await shell.Navigation.PushModalAsync(page);
        }
    }

    public async Task PopModalAsync()
    {
        if (Shell.Current is not null && Shell.Current.Navigation.ModalStack.Count > 0)
            await Shell.Current.Navigation.PopModalAsync();
    }

    public async Task GoToAsync(string route)
    {
        if (Shell.Current is not null)
            await Shell.Current.GoToAsync(route);
    }
}