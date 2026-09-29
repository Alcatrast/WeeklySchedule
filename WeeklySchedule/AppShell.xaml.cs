using System.ComponentModel;
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
        Routing.RegisterRoute(nameof(EditTimelinePage), typeof(EditTimelinePage));
        Routing.RegisterRoute(nameof(EditLessonPage), typeof(EditLessonPage));
        Routing.RegisterRoute(nameof(GroupSelectionPage), typeof(GroupSelectionPage));

        this.FlyoutWidth = 320;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SafeFireAndForget.Run(() => FlyoutVM.LoadTimelinesAsync());
        this.PropertyChanged += AppShell_PropertyChanged;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        this.PropertyChanged -= AppShell_PropertyChanged;
    }

    private void AppShell_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FlyoutIsPresented) && !FlyoutIsPresented) FlyoutVM.ResetEditMode();
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        if (args.Current?.Location.OriginalString.Contains("MainPage") == true)
        {
            SafeFireAndForget.Run(() => FlyoutVM.LoadTimelinesAsync());
        }
    }

    private void CloseFlyout() => this.FlyoutIsPresented = false;

    private void NavigateTo(string route)
    {
        FlyoutVM.ResetEditMode();
        CloseFlyout();
        SafeFireAndForget.Run(() => this.GoToAsync(route));
    }

    private void OnSettingsClicked(object? sender, TappedEventArgs e) => NavigateTo(nameof(SettingsPage));
    private void OnAboutClicked(object? sender, TappedEventArgs e) => NavigateTo(nameof(AboutPage));
}