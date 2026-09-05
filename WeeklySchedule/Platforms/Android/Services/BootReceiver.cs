using global::Android.App;
using global::Android.Content;

namespace WeeklySchedule.Platforms.Android.Services;

/// <summary>
/// Android стирает все будильники при перезагрузке телефона и при установке нового
/// apk поверх старого. Без этого приемника уведомления после каждого такого события
/// пропадали до следующего ручного запуска приложения.
///
/// Exported = true обязательно: широковещательное сообщение шлет система, а не мы.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = false)]
[IntentFilter([
    Intent.ActionBootCompleted,
    "android.intent.action.QUICKBOOT_POWERON",
    Intent.ActionMyPackageReplaced])]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent == null) return;

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[BOOT RECEIVER] {intent.Action}: восстанавливаем будильники");
#endif

        var alarms = ScheduledAlarmStore.Load();
        if (alarms.Count == 0) return;

        var nowMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const long weekMillis = 7L * 24 * 60 * 60 * 1000;

        foreach (var alarm in alarms)
        {
            // Телефон мог пролежать выключенным дольше недели: отматываем время
            // вперед недельными шагами, расписание все равно недельное
            if (alarm.TriggerAtMillis <= 0) continue;
            while (alarm.TriggerAtMillis <= nowMillis) alarm.TriggerAtMillis += weekMillis;

            NotificationService.SetAlarm(context, alarm);
        }

        ScheduledAlarmStore.Save(alarms);
    }
}
