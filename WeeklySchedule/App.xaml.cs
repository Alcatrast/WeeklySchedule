using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services, Services.ISettingsService settings)
    {
        InitializeComponent();
        UserAppTheme = settings.Theme;
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Резолвим AppShell через DI, чтобы передать ему зависимости
        var shell = _services.GetRequiredService<AppShell>();
        var window = new Window(shell);

        // Минутный таймер главного экрана останавливал только OnDisappearing страницы,
        // а приходит ли он при сворачивании приложения — вопрос спорный: комментарий в
        // GroupSelectionViewModel говорит, что приходит, а залипавшая подсказка на
        // экране разрешений говорила обратное. Окно отвечает на него однозначно, и
        // подписка здесь верна при любом ответе: обе стороны идемпотентны, а
        // InitializeDataAsync на неизменных данных идет по дешевой ветке
        window.Stopped += (_, _) => _services.GetRequiredService<MainViewModel>().StopMonitor();
        window.Resumed += (_, _) => SafeFireAndForget.Run(
            _services.GetRequiredService<MainViewModel>().InitializeDataAsync, "WindowResumed");
        return window;
    }
}
