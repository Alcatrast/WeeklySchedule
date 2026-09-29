using Android.App;
using Android.Runtime;
using WeeklySchedule.Platforms.Android.Services;
using WeeklySchedule.Services;

namespace WeeklySchedule.Platforms.Android
{
    [Application]
    public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
    {
        protected override MauiApp CreateMauiApp()
        {
            var app = MauiProgram.CreateMauiApp();

            var preferences = app.Services.GetRequiredService<IPreferencesService>();
            ScheduledAlarmStore.Initialize(preferences);

            return app;
        }
    }
}
