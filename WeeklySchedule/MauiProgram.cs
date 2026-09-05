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

        // ViewModels
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<FlyoutViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddTransient<TimelinesViewModel>();
        builder.Services.AddTransient<EditTimelineViewModel>();
        builder.Services.AddTransient<GroupSelectionViewModel>();

        // Pages (Все модальные страницы должны быть Transient!)
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<TimelinesPage>();
        builder.Services.AddTransient<EditTimelinePage>();
        builder.Services.AddTransient<EditLessonPage>();
        builder.Services.AddTransient<GroupSelectionPage>();
        builder.Services.AddSingleton<AboutPage>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
