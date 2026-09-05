using WeeklySchedule.ViewModels;
using WeeklySchedule.Views;

namespace WeeklySchedule;

public partial class AppShell : Shell
{
    public FlyoutViewModel FlyoutVM { get; }

    public AppShell(FlyoutViewModel flyoutVm)
    {
        FlyoutVM = flyoutVm;
        InitializeComponent();
        FlyoutGrid.BindingContext = FlyoutVM;

        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
        Routing.RegisterRoute(nameof(TimelinesPage), typeof(TimelinesPage));
        Routing.RegisterRoute(nameof(EditTimelinePage), typeof(EditTimelinePage));

        // УБИРАЕМ КОСТЫЛЬ С ИЗМЕРЕНИЕМ ТЕКСТА.
        // Flyout должен иметь фиксированную разумную ширину.
        this.FlyoutWidth = 320;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await FlyoutVM.LoadTimelinesAsync();
    }

    protected override async void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        if (args.Current?.Location.OriginalString.Contains("MainPage") == true)
        {
            await FlyoutVM.LoadTimelinesAsync();
        }
    }

    private void CloseFlyout() => this.FlyoutIsPresented = false;

    private async void OnTimelinesManageClicked(object? sender, TappedEventArgs e)
    {
        CloseFlyout();
        await this.GoToAsync(nameof(TimelinesPage));
    }

    private async void OnSettingsClicked(object? sender, TappedEventArgs e)
    {
        CloseFlyout();
        await this.GoToAsync(nameof(SettingsPage));
    }

    private async void OnAboutClicked(object? sender, TappedEventArgs e)
    {
        CloseFlyout();
        await this.GoToAsync(nameof(AboutPage));
    }
}