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
    internal const string ChannelId = "weekly_schedule_channel";
    private const string ChannelName = "Расписание";

    internal const string ActionShow = "com.weeklyschedule.SHOW_NOTIFICATION";

    private Context Context => Application.Context;

    // Список читается из SharedPreferences один раз за запуск: планирование идет
    // пачкой по всем парам, и перечитывать хранилище на каждую пару незачем.
    // Приемники пишут в то же хранилище и могут сделать кэш устаревшим, но набор
    // идентификаторов они не меняют — только время срабатывания, — поэтому отмена
    // по кэшу все равно попадает во все поставленные будильники
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
        // 1. Проверка POST_NOTIFICATIONS
        if (!await CheckPermissionAsync()) return false;

        // 2. Проверка SCHEDULE_EXACT_ALARM (Android 12+)
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var alarmManager = Context.GetSystemService(Context.AlarmService) as AlarmManager;
            if (alarmManager == null || !alarmManager.CanScheduleExactAlarms()) return false;
        }

        // 3. Проверка IGNORE_BATTERY_OPTIMIZATIONS (Android 6+)
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
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
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
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
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
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

    /// <summary>
    /// Идентификатор уведомления. Обязан быть одинаковым между запусками приложения:
    /// по нему отменяется ранее поставленный будильник. HashCode.Combine для этого
    /// не годится — он подмешивает случайное зерно, свое на каждый процесс, поэтому
    /// после перезапуска старые будильники становились неотменяемыми.
    /// </summary>
    internal static int BuildNotificationId(Guid lessonId, int minutesBefore)
    {
        Span<byte> bytes = stackalloc byte[16];
        lessonId.TryWriteBytes(bytes);

        unchecked
        {
            // FNV-1a
            uint hash = 2166136261;
            foreach (var b in bytes) hash = (hash ^ b) * 16777619;
            for (int i = 0; i < 4; i++) hash = (hash ^ (byte)(minutesBefore >> (i * 8))) * 16777619;

            // Гасим старший бит вместо Math.Abs: тот на int.MinValue бросает
            // OverflowException
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    /// <summary>
    /// Флаги PendingIntent. Обязаны совпадать у постановки, отмены и повтора из
    /// приемника: PendingIntent сопоставляется в том числе по ним, и разошедшиеся
    /// флаги дали бы отмену, которая промахивается мимо поставленного будильника.
    /// Immutable появился в API 23, а минимальная поддерживаемая версия — 21.
    /// </summary>
    internal static PendingIntentFlags BroadcastFlags =>
        OperatingSystem.IsAndroidVersionAtLeast(23)
            ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
            : PendingIntentFlags.UpdateCurrent;

    public void ScheduleNotification(Guid timelineId, Guid lessonId, string title, string body,
        DayOfWeek day, TimeSpan startTime, int minutesBefore)
    {
        int notificationId = BuildNotificationId(lessonId, minutesBefore);
        long triggerMillis = WeeklyOccurrence.Next(day, startTime,
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
            LessonDay = day,
            LessonStartTime = startTime
        };

        if (!SetAlarm(Context, alarm)) return;

        Alarms.RemoveAll(a => a.NotificationId == notificationId);
        Alarms.Add(alarm);
        ScheduledAlarmStore.Save(Alarms);
    }

    /// <summary>
    /// Ставит будильник в AlarmManager. Статический, потому что тем же кодом
    /// восстанавливает будильники BootReceiver, у которого нет сервиса из DI.
    /// </summary>
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

        var pendingIntent = PendingIntent.GetBroadcast(context, alarm.NotificationId, intent, BroadcastFlags);
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

        // Extras при сопоставлении PendingIntent не учитываются, поэтому для отмены
        // достаточно совпадения requestCode, компонента, действия и флагов
        var pendingIntent = PendingIntent.GetBroadcast(Context, notificationId, intent, BroadcastFlags);
        if (pendingIntent == null) return;

        alarmManager.Cancel(pendingIntent);
        pendingIntent.Cancel();
    }
    #endregion
}
