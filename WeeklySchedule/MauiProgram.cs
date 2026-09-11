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

        builder.Services.AddSingleton<ILessonRepository, FileLessonRepository>();
        builder.Services.AddSingleton<ITimelineRepository, FileTimelineRepository>();
        builder.Services.AddSingleton<IDataSeeder, DemoDataSeeder>();

        builder.Services.AddSingleton<IActiveScheduleService, ActiveScheduleService>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        builder.Services.AddSingleton<INotificationNavigationService, NotificationNavigationService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();

        builder.Services.AddSingleton<IEditLessonPageFactory, EditLessonPageFactory>();
        builder.Services.AddSingleton<IGroupSelectionPageFactory, GroupSelectionPageFactory>();

#if ANDROID
        builder.Services.AddSingleton<INotificationService, WeeklySchedule.Platforms.Android.Services.NotificationService>();
#else
        builder.Services.AddSingleton<INotificationService, MockNotificationService>();
#endif

        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<FlyoutViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddTransient<TimelinesViewModel>();
        builder.Services.AddTransient<EditTimelineViewModel>();
        builder.Services.AddTransient<GroupSelectionViewModel>();

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