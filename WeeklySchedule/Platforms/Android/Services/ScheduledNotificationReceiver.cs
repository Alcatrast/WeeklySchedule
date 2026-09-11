using Android.OS;
using global::Android.App;
using global::Android.Content;
using global::AndroidX.Core.App;
using Application = global::Android.App.Application;

namespace WeeklySchedule.Platforms.Android.Services;

[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(["com.weeklyschedule.SHOW_NOTIFICATION"])]
public class ScheduledNotificationReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent == null) return;

        try
        {
            Receive(context, intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NOTIF RECEIVER] {ex}");
        }
    }

    private static void Receive(Context context, Intent intent)
    {
        var title = intent.GetStringExtra("Title") ?? "Пара";
        var body = intent.GetStringExtra("Body") ?? "";
        var timelineId = intent.GetStringExtra("TimelineId") ?? "";
        var lessonId = intent.GetStringExtra("LessonId") ?? "";
        var notificationId = intent.GetIntExtra("NotificationId", 0);
        var minutesBefore = intent.GetIntExtra("MinutesBefore", 0);
        var triggerAtMillis = intent.GetLongExtra("TriggerAtMillis", 0);

        var alarm = ScheduledAlarmStore.Load().FirstOrDefault(a => a.NotificationId == notificationId);

        if (alarm == null || alarm.TriggerAtMillis != triggerAtMillis) return;

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[NOTIF RECEIVER] Сработал! ID: {notificationId}, Title: {title}");
#endif

        RescheduleNextWeek(context, alarm);

        var appIntent = new Intent(context, typeof(MainActivity));
        appIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        appIntent.PutExtra("TimelineId", timelineId);

        var pendingIntentFlags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            pendingIntentFlags |= PendingIntentFlags.Immutable;
        }

        var pendingAppIntent = PendingIntent.GetActivity(context, notificationId, appIntent, pendingIntentFlags);

        var builder = new NotificationCompat.Builder(context, "weekly_schedule_channel")?
            .SetAutoCancel(true)?
            .SetSmallIcon(global::Android.Resource.Drawable.IcPopupReminder)?
            .SetContentTitle(title)?
            .SetContentText(body)?
            .SetContentIntent(pendingAppIntent)?
            .SetPriority(NotificationCompat.PriorityHigh)?
            .SetCategory(NotificationCompat.CategoryAlarm);

        var manager = NotificationManagerCompat.From(context);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var hasPermission = context.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications)
                                == global::Android.Content.PM.Permission.Granted;
            if (!hasPermission)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[NOTIF RECEIVER] Пропущено: нет разрешения POST_NOTIFICATIONS.");
#endif
                return;
            }
        }

        manager?.Notify(notificationId, builder?.Build());

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[NOTIF RECEIVER] Уведомление успешно отправлено в NotificationManager.");
#endif
    }

    private static void RescheduleNextWeek(Context context, ScheduledAlarm alarm)
    {
        var now = DateTimeOffset.UtcNow;
        var previous = DateTimeOffset.FromUnixTimeMilliseconds(alarm.TriggerAtMillis);
        if (!alarm.MoveToNextOccurrence(now > previous ? now : previous, TimeZoneInfo.Local)) return;
        if (!NotificationService.SetAlarm(context, alarm)) return;

        var alarms = ScheduledAlarmStore.Load();
        alarms.RemoveAll(a => a.NotificationId == alarm.NotificationId);
        alarms.Add(alarm);
        ScheduledAlarmStore.Save(alarms);
    }
}