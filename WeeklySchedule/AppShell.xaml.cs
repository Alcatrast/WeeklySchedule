using WeeklySchedule.Utilities;
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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SafeFireAndForget.Run(FlyoutVM.LoadTimelinesAsync);
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        if (args.Current?.Location.OriginalString.Contains("MainPage") == true)
        {
            SafeFireAndForget.Run(FlyoutVM.LoadTimelinesAsync);
        }
    }

    private void CloseFlyout() => this.FlyoutIsPresented = false;

    private void NavigateTo(string route)
    {
        CloseFlyout();
        SafeFireAndForget.Run(() => this.GoToAsync(route));
    }

    private void OnTimelinesManageClicked(object? sender, TappedEventArgs e) => NavigateTo(nameof(TimelinesPage));

    private void OnSettingsClicked(object? sender, TappedEventArgs e) => NavigateTo(nameof(SettingsPage));

    private void OnAboutClicked(object? sender, TappedEventArgs e) => NavigateTo(nameof(AboutPage));
}