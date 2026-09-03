using Android.App;
using Android.Content.PM;
using Android.OS;
using WeeklySchedule.Services;

namespace WeeklySchedule;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        HandleNotificationIntent(Intent);
    }

    protected override void OnNewIntent(Android.Content.Intent intent)
    {
        base.OnNewIntent(intent);
        HandleNotificationIntent(intent);
    }

    private void HandleNotificationIntent(Android.Content.Intent? intent)
    {
        if (intent?.HasExtra("TimelineId") == true)
        {
            var idStr = intent.GetStringExtra("TimelineId");
            if (Guid.TryParse(idStr, out var timelineId))
            {
                var navService = IPlatformApplication.Current?.Services.GetService<INotificationNavigationService>();
                navService?.SetPendingNavigation(timelineId);
            }
        }
    }

    // Обработка результата запроса разрешений
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        // Здесь можно вызвать событие, чтобы UI обновился
    }
}