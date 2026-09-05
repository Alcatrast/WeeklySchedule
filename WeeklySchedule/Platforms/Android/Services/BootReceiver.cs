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
    Intent.ActionMyPackageReplaced,
    Intent.ActionTimezoneChanged,
    Intent.ActionTimeChanged])]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent == null) return;
        if (intent.Action is not (Intent.ActionBootCompleted or "android.intent.action.QUICKBOOT_POWERON"
            or Intent.ActionMyPackageReplaced or Intent.ActionTimezoneChanged or Intent.ActionTimeChanged)) return;

        TimeZoneInfo.ClearCachedData();

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[BOOT RECEIVER] {intent.Action}: восстанавливаем будильники");
#endif

        var alarms = ScheduledAlarmStore.Load();
        if (alarms.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        var zone = TimeZoneInfo.Local;

        foreach (var alarm in alarms)
        {
            try
            {
                if (alarm.MoveToNextOccurrence(now, zone)) NotificationService.SetAlarm(context, alarm);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BOOT RECEIVER] {alarm.NotificationId}: {ex}");
            }
        }

        ScheduledAlarmStore.Save(alarms);
    }
}
