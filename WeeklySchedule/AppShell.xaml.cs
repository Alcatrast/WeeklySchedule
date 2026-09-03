using System.Collections.Specialized;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Services;
using WeeklySchedule.ViewModels;
using WeeklySchedule.Views;

namespace WeeklySchedule;

public partial class AppShell : Shell
{
    public FlyoutViewModel FlyoutVM { get; }

    public AppShell(
        ITimelineRepository repo,
        IActiveScheduleService sched,
        ISettingsService settings,
        FlyoutViewModel flyoutVm)
    {
        FlyoutVM = flyoutVm;
        InitializeComponent();
        FlyoutGrid.BindingContext = FlyoutVM;

        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
        Routing.RegisterRoute(nameof(TimelinesPage), typeof(TimelinesPage));
        Routing.RegisterRoute(nameof(EditTimelinePage), typeof(EditTimelinePage));

        FlyoutVM.Timelines.CollectionChanged += Timelines_CollectionChanged;

        // УБИРАЕМ КОСТЫЛЬ С ИЗМЕРЕНИЕМ ТЕКСТА. 
        // Flyout должен иметь фиксированную разумную ширину.
        this.FlyoutWidth = 320;
    }

    private void Timelines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Больше не пересчитываем ширину. Это было ошибкой архитектуры.
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