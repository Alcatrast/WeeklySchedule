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

        // EditTimelinePage маршрута не имеет: ее открывает ItemActions модально
        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
        Routing.RegisterRoute(nameof(TimelinesPage), typeof(TimelinesPage));

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
                // Шторка уезжает в том же кадре, что и отпускание пальца: подготовка
                // значений идет параллельно ее анимации, а не вместо нее. Раньше меню
                // стояло открытым и неподвижным, пока читались настройки.
                CloseFlyout();
                // Значения по-прежнему готовы до создания страницы — его делает GoToAsync,
                // а не CloseFlyout, — иначе блок разрешений и списки меняли бы её размеры
                // посреди открытия. Хендлер может быть еще не готов: тогда переход важнее
                // подготовки, а раньше тап в этом случае молча не делал ничего.
                if (route == nameof(SettingsPage) && Handler?.MauiContext?.Services is { } services)
                    await services.GetRequiredService<SettingsViewModel>().RefreshIfStaleAsync();
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
