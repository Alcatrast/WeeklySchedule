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
        SafeFireAndForget.Run(FlyoutVM.RefreshIfNeededAsync);
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        if (args.Current?.Location.OriginalString.Contains("MainPage") == true)
        {
            SafeFireAndForget.Run(FlyoutVM.RefreshIfNeededAsync);
        }
    }

    private void CloseFlyout() => this.FlyoutIsPresented = false;
    private bool _navigating;

    private void NavigateTo(string route)
    {
        if (_navigating) return;
        _navigating = true;
        SafeFireAndForget.Run(async () =>
        {
            try
            {
                // Готовим значения до создания страницы, чтобы блок разрешений
                // и списки не меняли её размеры посреди открытия.
                if (route == nameof(SettingsPage))
                    await Handler!.MauiContext!.Services.GetRequiredService<SettingsViewModel>().RefreshAsync();
                CloseFlyout();
                // Остаётся анимация меню; второй сдвиг страницы поверх неё не нужен.
                await GoToAsync(route, animate: false);
            }
            finally { _navigating = false; }
        });
    }

    private void OnTimelinesManageClicked(object? sender, TappedEventArgs e) => NavigateTo(nameof(TimelinesPage));

    private void OnSettingsClicked(object? sender, TappedEventArgs e) => NavigateTo(nameof(SettingsPage));

    private void OnAboutClicked(object? sender, TappedEventArgs e) => NavigateTo(nameof(AboutPage));
}
