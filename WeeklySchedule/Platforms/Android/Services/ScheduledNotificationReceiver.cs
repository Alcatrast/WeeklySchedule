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
        if (context == null || intent == null) return;

        var title = intent.GetStringExtra("Title") ?? "Пара";
        var body = intent.GetStringExtra("Body") ?? "";
        var timelineId = intent.GetStringExtra("TimelineId") ?? "";
        var notificationId = intent.GetIntExtra("NotificationId", 0);
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[NOTIF RECEIVER] Сработал! ID: {notificationId}, Title: {title}");
#endif

        // Intent для открытия приложения при клике на уведомление
        var appIntent = new Intent(context, typeof(MainActivity));
        appIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        appIntent.PutExtra("TimelineId", timelineId);

        var pendingIntentFlags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
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
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
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
}