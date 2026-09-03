using global::Android.App;
using global::Android.Content;
using global::Android.OS;
using global::AndroidX.Core.App;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using Application = global::Android.App.Application;

namespace WeeklySchedule.Platforms.Android.Services;

public class NotificationService : INotificationService
{
    private const string ChannelId = "weekly_schedule_channel";
    private const string ChannelName = "Расписание";
    private const string ActionShow = "com.weeklyschedule.SHOW_NOTIFICATION";
    private readonly List<int> _scheduledIds = new();
    private Context Context => Application.Context;

    public NotificationService() => CreateNotificationChannel();

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.High);
            var manager = Context.GetSystemService(Context.NotificationService) as NotificationManager;
            manager?.CreateNotificationChannel(channel);
        }
    }

    #region Базовые разрешения (POST_NOTIFICATIONS)
    public Task<bool> CheckPermissionAsync()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu) return Task.FromResult(true);
        var status = Context.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications);
        return Task.FromResult(status == global::Android.Content.PM.Permission.Granted);
    }

    public Task RequestPermissionAsync()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu) return Task.CompletedTask;
        var activity = Platform.CurrentActivity;
        if (activity != null)
        {
            global::AndroidX.Core.App.ActivityCompat.RequestPermissions(
                activity,
                new[] { global::Android.Manifest.Permission.PostNotifications },
                101);
        }
        return Task.CompletedTask;
    }
    #endregion

    #region Комплексная проверка и запрос всех 3 разрешений
    public async Task<bool> CheckAllPermissionsAsync()
    {
        // 1. Проверка POST_NOTIFICATIONS
        if (!await CheckPermissionAsync()) return false;

        // 2. Проверка SCHEDULE_EXACT_ALARM (Android 12+)
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            var alarmManager = Context.GetSystemService(Context.AlarmService) as AlarmManager;
            if (alarmManager == null || !alarmManager.CanScheduleExactAlarms()) return false;
        }

        // 3. Проверка IGNORE_BATTERY_OPTIMIZATIONS (Android 6+)
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            var powerManager = Context.GetSystemService(Context.PowerService) as PowerManager;
            if (powerManager == null || !powerManager.IsIgnoringBatteryOptimizations(Context.PackageName)) return false;
        }

        return true;
    }

    public async Task RequestAllPermissionsAsync()
    {
        // 1. Запрос POST_NOTIFICATIONS
        await RequestPermissionAsync();
        await Task.Delay(800); // Пауза для взаимодействия с системным диалогом

        // 2. Запрос SCHEDULE_EXACT_ALARM
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            var alarmManager = Context.GetSystemService(Context.AlarmService) as AlarmManager;
            if (alarmManager != null && !alarmManager.CanScheduleExactAlarms())
            {
                // ИСПРАВЛЕНО: добавлен префикс global:: для избежания конфликта пространств имен
                var intent = new Intent(global::Android.Provider.Settings.ActionRequestScheduleExactAlarm);
                intent.SetData(global::Android.Net.Uri.Parse("package:" + Context.PackageName));
                StartSettingsActivity(intent);
                await Task.Delay(1000);
            }
        }

        // 3. Запрос IGNORE_BATTERY_OPTIMIZATIONS
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            var powerManager = Context.GetSystemService(Context.PowerService) as PowerManager;
            if (powerManager != null && !powerManager.IsIgnoringBatteryOptimizations(Context.PackageName))
            {
                // ИСПРАВЛЕНО: добавлен префикс global:: для избежания конфликта пространств имен
                var intent = new Intent(global::Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
                intent.SetData(global::Android.Net.Uri.Parse("package:" + Context.PackageName));
                StartSettingsActivity(intent);
                await Task.Delay(1000);
            }
        }
    }

    private void StartSettingsActivity(Intent intent)
    {
        var activity = Platform.CurrentActivity;
        if (activity != null)
        {
            activity.StartActivity(intent);
        }
        else
        {
            intent.AddFlags(ActivityFlags.NewTask);
            Application.Context.StartActivity(intent);
        }
    }
    #endregion

    #region Логика будильников
    public void ScheduleNotification(Guid timelineId, Guid lessonId, string title, string body, DateTime triggerTime, int minutesBefore)
    {
        var actualTriggerTime = triggerTime.AddMinutes(-minutesBefore);
        if (actualTriggerTime <= DateTime.Now) return;

        int notificationId = Math.Abs(HashCode.Combine(lessonId.GetHashCode(), minutesBefore));
        if (!_scheduledIds.Contains(notificationId)) _scheduledIds.Add(notificationId);

        var intent = new Intent(Context, typeof(ScheduledNotificationReceiver));
        intent.SetAction(ActionShow);
        intent.PutExtra("Title", title);
        intent.PutExtra("Body", body);
        intent.PutExtra("TimelineId", timelineId.ToString());
        intent.PutExtra("NotificationId", notificationId);

        var pendingIntent = PendingIntent.GetBroadcast(Context, notificationId, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var alarmManager = Context.GetSystemService(Context.AlarmService) as AlarmManager;

        long triggerMillis = new DateTimeOffset(actualTriggerTime).ToUnixTimeMilliseconds();

        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            if (alarmManager?.CanScheduleExactAlarms() == true)
                alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMillis, pendingIntent);
            else
                alarmManager?.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMillis, pendingIntent);
        }
        else
        {
            alarmManager?.SetExact(AlarmType.RtcWakeup, triggerMillis, pendingIntent);
        }
    }

    public void CancelNotificationsForLesson(Guid lessonId) { }

    public void CancelAllNotifications()
    {
        var alarmManager = Context.GetSystemService(Context.AlarmService) as AlarmManager;
        if (alarmManager == null) return;
        var intent = new Intent(Context, typeof(ScheduledNotificationReceiver));
        intent.SetAction(ActionShow);
        foreach (var id in _scheduledIds)
        {
            var pendingIntent = PendingIntent.GetBroadcast(Context, id, intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            alarmManager.Cancel(pendingIntent);
        }
        _scheduledIds.Clear();
    }
    #endregion
}