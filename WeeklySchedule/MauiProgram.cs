using Microsoft.Extensions.Logging;
using WeeklySchedule.Data;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Services;
using WeeklySchedule.ViewModels;
using WeeklySchedule.Views;

namespace WeeklySchedule;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Repositories
        builder.Services.AddSingleton<ILessonRepository, FileLessonRepository>();
        builder.Services.AddSingleton<ITimelineRepository, FileTimelineRepository>();
        builder.Services.AddSingleton<IDataSeeder, EmptyDataSeeder>();

        // Services
        builder.Services.AddSingleton<IActiveScheduleService, ActiveScheduleService>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        builder.Services.AddSingleton<INotificationNavigationService, NotificationNavigationService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<ItemDeletionService>();

#if ANDROID
        builder.Services.AddSingleton<INotificationService, WeeklySchedule.Platforms.Android.Services.NotificationService>();
#else
        builder.Services.AddSingleton<INotificationService, MockNotificationService>();
#endif

        // Конвертеры в DI не нужны: XAML берет их из App.xaml как StaticResource,
        // а код в DayView держит собственные статические экземпляры

        // ViewModels. EditTimelineViewModel и GroupSelectionViewModel здесь не
        // регистрируются: им нужны Timeline и путь к файлу, которых в контейнере
        // нет, так что резолв все равно кончился бы исключением. Их создают руками
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<FlyoutViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddTransient<TimelinesViewModel>();

        // Страницы, которые открывает Shell или DI. EditLessonPage и
        // GroupSelectionPage создаются через new с параметрами конкретной пары
        // или файла, поэтому в контейнере им тоже не место
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<TimelinesPage>();
        builder.Services.AddTransient<EditTimelinePage>();
        builder.Services.AddSingleton<AboutPage>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
