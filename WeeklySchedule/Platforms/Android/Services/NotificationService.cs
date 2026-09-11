using global::Android.App;
using global::Android.Content;
using global::Android.OS;
using global::AndroidX.Core.App;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using Application = global::Android.App.Application;

namespace WeeklySchedule.Platforms.Android.Services;

public class NotificationService : INotificationService
{
    private const string ChannelId = "weekly_schedule_channel";
    private const string ChannelName = "Расписание";

    internal const string ActionShow = "com.weeklyschedule.SHOW_NOTIFICATION";

    private Context Context => Application.Context;
    private List<ScheduledAlarm>? _alarms;
    private List<ScheduledAlarm> Alarms => _alarms ??= ScheduledAlarmStore.Load();

    public NotificationService() => CreateNotificationChannel();

    private void CreateNotificationChannel()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.High);
            var manager = Context.GetSystemService(Context.NotificationService) as NotificationManager;
            manager?.CreateNotificationChannel(channel);
        }
    }

    #region Базовые разрешения (POST_NOTIFICATIONS)
    public Task<bool> CheckPermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return Task.FromResult(true);
        var status = Context.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications);
        return Task.FromResult(status == global::Android.Content.PM.Permission.Granted);
    }

    public Task RequestPermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return Task.CompletedTask;
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
        if (!await CheckPermissionAsync()) return false;

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var alarmManager = Context.GetSystemService(Context.AlarmService) as AlarmManager;
            if (alarmManager == null || !alarmManager.CanScheduleExactAlarms()) return false;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            var powerManager = Context.GetSystemService(Context.PowerService) as PowerManager;
            if (powerManager == null || !powerManager.IsIgnoringBatteryOptimizations(Context.PackageName)) return false;
        }

        return true;
    }

    public async Task RequestAllPermissionsAsync()
    {
        await RequestPermissionAsync();
        await Task.Delay(800);

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var alarmManager = Context.GetSystemService(Context.AlarmService) as AlarmManager;
            if (alarmManager != null && !alarmManager.CanScheduleExactAlarms())
            {
                var intent = new Intent(global::Android.Provider.Settings.ActionRequestScheduleExactAlarm);
                intent.SetData(global::Android.Net.Uri.Parse("package:" + Context.PackageName));
                StartSettingsActivity(intent);
                await Task.Delay(1000);
            }
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            var powerManager = Context.GetSystemService(Context.PowerService) as PowerManager;
            if (powerManager != null && !powerManager.IsIgnoringBatteryOptimizations(Context.PackageName))
            {
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
    internal static int BuildNotificationId(Guid lessonId, int minutesBefore)
    {
        var bytes = lessonId.ToByteArray();
        int hash = BitConverter.ToInt32(bytes, 0) ^
                   BitConverter.ToInt32(bytes, 4) ^
                   BitConverter.ToInt32(bytes, 8) ^
                   BitConverter.ToInt32(bytes, 12) ^
                   minutesBefore;
        return hash & 0x7FFFFFFF;
    }

    public void ScheduleNotification(Guid timelineId, Guid lessonId, string title, string body, DateTime triggerTime, int minutesBefore)
    {
        int notificationId = BuildNotificationId(lessonId, minutesBefore);
        long triggerMillis = WeeklyOccurrence.Next(triggerTime.DayOfWeek, triggerTime.TimeOfDay,
            minutesBefore, DateTimeOffset.Now, TimeZoneInfo.Local).ToUnixTimeMilliseconds();

        var alarm = new ScheduledAlarm
        {
            NotificationId = notificationId,
            TimelineId = timelineId.ToString(),
            LessonId = lessonId.ToString(),
            Title = title,
            Body = body,
            TriggerAtMillis = triggerMillis,
            MinutesBefore = minutesBefore,
            LessonDay = triggerTime.DayOfWeek,
            LessonStartTime = triggerTime.TimeOfDay
        };

        if (!SetAlarm(Context, alarm)) return;

        Alarms.RemoveAll(a => a.NotificationId == notificationId);
        Alarms.Add(alarm);
        ScheduledAlarmStore.Save(Alarms);
    }

    internal static bool SetAlarm(Context context, ScheduledAlarm alarm)
    {
        if (context.GetSystemService(Context.AlarmService) is not AlarmManager alarmManager) return false;

        var intent = new Intent(context, typeof(ScheduledNotificationReceiver));
        intent.SetAction(ActionShow);
        intent.PutExtra("Title", alarm.Title);
        intent.PutExtra("Body", alarm.Body);
        intent.PutExtra("TimelineId", alarm.TimelineId);
        intent.PutExtra("LessonId", alarm.LessonId);
        intent.PutExtra("NotificationId", alarm.NotificationId);
        intent.PutExtra("MinutesBefore", alarm.MinutesBefore);
        intent.PutExtra("TriggerAtMillis", alarm.TriggerAtMillis);

        var pendingIntent = PendingIntent.GetBroadcast(context, alarm.NotificationId, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        if (pendingIntent == null) return false;

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            if (alarmManager.CanScheduleExactAlarms())
                alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, alarm.TriggerAtMillis, pendingIntent);
            else
                alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, alarm.TriggerAtMillis, pendingIntent);
        }
        else
        {
            alarmManager.SetExact(AlarmType.RtcWakeup, alarm.TriggerAtMillis, pendingIntent);
        }

        return true;
    }

    public void CancelNotificationsForLesson(Guid lessonId)
    {
        var key = lessonId.ToString();

        var toCancel = Alarms.Where(a => a.LessonId == key).ToList();
        if (toCancel.Count == 0) return;

        foreach (var alarm in toCancel) CancelAlarm(alarm.NotificationId);

        Alarms.RemoveAll(a => a.LessonId == key);
        ScheduledAlarmStore.Save(Alarms);
    }

    public void CancelAllNotifications()
    {
        foreach (var alarm in Alarms) CancelAlarm(alarm.NotificationId);
        Alarms.Clear();
        ScheduledAlarmStore.Save(Alarms);
    }

    private void CancelAlarm(int notificationId)
    {
        if (Context.GetSystemService(Context.AlarmService) is not AlarmManager alarmManager) return;

        var intent = new Intent(Context, typeof(ScheduledNotificationReceiver));
        intent.SetAction(ActionShow);

        var pendingIntent = PendingIntent.GetBroadcast(Context, notificationId, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        if (pendingIntent == null) return;

        alarmManager.Cancel(pendingIntent);
        pendingIntent.Cancel();
    }
    #endregion
}
