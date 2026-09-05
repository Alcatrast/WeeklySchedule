using Android.OS;
using global::Android.App;
using global::Android.Content;
using global::AndroidX.Core.App;
using Application = global::Android.App.Application;

namespace WeeklySchedule.Platforms.Android.Services;

// Exported = false, так как receiver используется только внутри приложения
[BroadcastReceiver(Enabled = true, Exported = false)]
// ВАЖНО: Action должен ТОЧНО совпадать с ActionShow в NotificationService
[IntentFilter(new[] { "com.weeklyschedule.SHOW_NOTIFICATION" })]
public class ScheduledNotificationReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        try { Receive(context, intent); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[NOTIF RECEIVER] {ex}"); }
    }

    private static void Receive(Context? context, Intent? intent)
    {
        if (context == null || intent == null) return;

        var title = intent.GetStringExtra("Title") ?? "Пара";
        var body = intent.GetStringExtra("Body") ?? "";
        var timelineId = intent.GetStringExtra("TimelineId") ?? "";
        var lessonId = intent.GetStringExtra("LessonId") ?? "";
        var notificationId = intent.GetIntExtra("NotificationId", 0);
        var minutesBefore = intent.GetIntExtra("MinutesBefore", 0);
        var triggerAtMillis = intent.GetLongExtra("TriggerAtMillis", 0);
        var alarm = ScheduledAlarmStore.Load().FirstOrDefault(a => a.NotificationId == notificationId);
        // Уже отмененная или замененная доставка не должна воскресить будильник.
        if (alarm == null || alarm.TriggerAtMillis != triggerAtMillis) return;
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[NOTIF RECEIVER] Сработал! ID: {notificationId}, Title: {title}");
#endif

        // Расписание недельное, а AlarmManager умеет только разовые точные будильники:
        // повтор ставим сами, здесь. Раньше повтора не было вообще, и без запуска
        // приложения уведомления заканчивались через неделю
        RescheduleNextWeek(context, alarm);

        // Intent для открытия приложения при клике на уведомление
        var appIntent = new Intent(context, typeof(MainActivity));
        appIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        appIntent.PutExtra("TimelineId", timelineId);

        var pendingIntentFlags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            pendingIntentFlags |= PendingIntentFlags.Immutable;
        }

        var pendingAppIntent = PendingIntent.GetActivity(context, notificationId, appIntent, pendingIntentFlags);

        // Построение уведомления
        var builder = new NotificationCompat.Builder(context, "weekly_schedule_channel")
            .SetAutoCancel(true)
            .SetSmallIcon(global::Android.Resource.Drawable.IcPopupReminder) // Можно заменить на вашу иконку, если добавите в drawable
            .SetContentTitle(title)
            .SetContentText(body)
            .SetContentIntent(pendingAppIntent)
            .SetPriority(NotificationCompat.PriorityHigh) // Высокий приоритет для всплывающих уведомлений
            .SetCategory(NotificationCompat.CategoryAlarm);

        var manager = NotificationManagerCompat.From(context);

        // Проверка разрешения на показ уведомлений (Android 13+)
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

        manager.Notify(notificationId, builder.Build());
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[NOTIF RECEIVER] Уведомление успешно отправлено в NotificationManager.");
#endif
    }

    /// <summary>
    /// Переставляет тот же будильник на следующую неделю и обновляет запись
    /// в хранилище, чтобы после перезагрузки восстановилось актуальное время.
    /// </summary>
    private static void RescheduleNextWeek(Context context, ScheduledAlarm alarm)
    {
        // Приложение могло не запускаться несколько недель — отматываем вперед,
        // пока время не окажется в будущем
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
