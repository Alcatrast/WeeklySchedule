using global::Android.App;
using global::Android.Content;
using Microsoft.Extensions.Logging;
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

    // Постановка пакета идет в фоновом потоке, и два пакета не должны перемешаться:
    // между отменой прежних будильников и записью новых состояние хранилища не
    // соответствует системе
    private readonly Lock _applyLock = new();

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

    /// <summary>
    /// Запрос идет через Essentials, а не через ActivityCompat напрямую: тот
    /// показывает диалог и уходит, не сказав ответа, и вызывающему оставалось только
    /// подождать наугад и перечитать состояние. Здесь возвращается настоящий итог —
    /// MauiAppCompatActivity прокидывает OnRequestPermissionsResult в Essentials сам.
    /// </summary>
    public async Task<bool> RequestPermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return true;
        var status = await Permissions.RequestAsync<Permissions.PostNotifications>();
        return status == PermissionStatus.Granted;
    }

    /// <summary>
    /// Отклоненное дважды разрешение система больше не спрашивает: диалог не
    /// появляется, а запрос возвращается мгновенно. Единственный оставшийся путь —
    /// системный экран уведомлений приложения.
    /// </summary>
    public Task<bool> OpenNotificationSettingsAsync()
    {
        Intent intent;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            intent = new Intent(global::Android.Provider.Settings.ActionAppNotificationSettings);
            intent.PutExtra(global::Android.Provider.Settings.ExtraAppPackage, Context.PackageName);
        }
        else
        {
            intent = new Intent(global::Android.Provider.Settings.ActionApplicationDetailsSettings);
            intent.SetData(global::Android.Net.Uri.Parse("package:" + Context.PackageName));
        }
        return Task.FromResult(StartSettingsActivity(intent));
    }
    #endregion

    #region Точность будильников (SCHEDULE_EXACT_ALARM)
    /// <summary>
    /// Приложение не просит ни исключения из оптимизации батареи, ни права работать
    /// в фоне, и не держит ни сервиса, ни цикла: расписание будильников хранит сама
    /// система, процесс до срабатывания не живет. Doze доставку не отменяет —
    /// оба Set*AndAllowWhileIdle в <see cref="SetAlarm"/> пробивают простой по
    /// определению, но пропускает такой будильник примерно раз в девять минут на
    /// приложение, отчего без точности уведомление и опаздывает до десяти минут.
    /// Исключение из оптимизации к доставке ничего не добавляло, только показывало
    /// пугающий системный диалог.
    ///
    /// Точность же нужна не для доставки, а для минуты в минуту. На Android 13+ ее
    /// дает USE_EXACT_ALARM, выданный молча при установке, так что спрашивать нечего.
    /// Экран настроек нужен фактически только на Android 12.
    /// </summary>
    public Task<bool> CanScheduleExactAlarmsAsync()
    {
        // До Android 12 точный будильник ставится без разрешения вообще
        if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return Task.FromResult(true);

        var alarmManager = Context.GetSystemService(Context.AlarmService) as AlarmManager;
        return Task.FromResult(alarmManager?.CanScheduleExactAlarms() ?? false);
    }

    public Task<bool> RequestExactAlarmsAsync()
    {
        // Ниже Android 12 точность есть всегда, и на 12+ при уже выданном разрешении
        // открывать нечего — в обоих случаях состояние в порядке, а не сломано
        if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return Task.FromResult(true);

        var alarmManager = Context.GetSystemService(Context.AlarmService) as AlarmManager;
        if (alarmManager == null) return Task.FromResult(false);
        if (alarmManager.CanScheduleExactAlarms()) return Task.FromResult(true);

        // Разрешение выдается только на системном экране, диалога у него нет
        var intent = new Intent(global::Android.Provider.Settings.ActionRequestScheduleExactAlarm);
        intent.SetData(global::Android.Net.Uri.Parse("package:" + Context.PackageName));
        return Task.FromResult(StartSettingsActivity(intent));
    }

    /// <summary>
    /// Экрана из интента может не оказаться — на части прошивок его нет вовсе, — и
    /// StartActivity тогда бросает ActivityNotFoundException. Исключение отсюда
    /// улетало в SafeFireAndForget через команду и попадало в лог под именем
    /// конструктора вью-модели, так что по следу нельзя было понять даже экран.
    /// </summary>
    private bool StartSettingsActivity(Intent intent)
    {
        try
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
            return true;
        }
        catch (Exception ex)
        {
            SafeFireAndForget.Logger?.LogError(ex, "Не удалось открыть системный экран {Action}", intent.Action);
            return false;
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

    /// <summary>
    /// Ставит ровно переданный набор вместо всех прежних. Работа уходит в фоновый
    /// поток целиком: на каждое напоминание приходятся два обращения к системе, и
    /// шесть десятков будильников с главного потока замораживали экран.
    /// </summary>
    public Task ReplaceScheduledAsync(IReadOnlyList<PlannedNotification> plan) =>
        Task.Run(() => ReplaceScheduled(plan));

    private void ReplaceScheduled(IReadOnlyList<PlannedNotification> plan)
    {
        lock (_applyLock)
        {
            var context = Context;
            // Список читается заново, а не из кэша: приемник переставляет сработавший
            // будильник на следующую неделю в этом же процессе и пишет в то же хранилище
            foreach (var stale in ScheduledAlarmStore.Load()) CancelAlarm(context, stale.NotificationId);

            var armed = new List<ScheduledAlarm>(plan.Count);
            var ids = new HashSet<int>();
            foreach (var item in plan)
            {
                // Два напоминания на одинаковое число минут дают один и тот же
                // идентификатор: второе перезаписало бы первое в системе, а в
                // хранилище осталась бы лишняя запись
                int notificationId = BuildNotificationId(item.LessonId, item.MinutesBefore);
                if (!ids.Add(notificationId)) continue;

                try
                {
                    var alarm = new ScheduledAlarm
                    {
                        NotificationId = notificationId,
                        TimelineId = item.TimelineId.ToString(),
                        LessonId = item.LessonId.ToString(),
                        Title = item.Title,
                        Body = item.Body,
                        TriggerAtMillis = WeeklyOccurrence.Next(item.Day, item.StartTime,
                            item.MinutesBefore, DateTimeOffset.Now, TimeZoneInfo.Local).ToUnixTimeMilliseconds(),
                        MinutesBefore = item.MinutesBefore,
                        LessonDay = item.Day,
                        LessonStartTime = item.StartTime
                    };
                    if (SetAlarm(context, alarm)) armed.Add(alarm);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    // Испорченное время одной пары не должно оставлять без будильников
                    // все остальные
                    SafeFireAndForget.Logger?.LogError(ex,
                        "Будильник для пары {Lesson} за {Minutes} мин. не поставлен",
                        item.LessonId, item.MinutesBefore);
                }
            }

            // Одна запись на весь набор. Раньше хранилище переписывалось после каждого
            // будильника: тридцать пар с одним напоминанием давали шестьдесят записей
            // растущего списка целиком
            ScheduledAlarmStore.Save(armed);
        }
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

    private static void CancelAlarm(Context context, int notificationId)
    {
        if (context.GetSystemService(Context.AlarmService) is not AlarmManager alarmManager) return;

        var intent = new Intent(context, typeof(ScheduledNotificationReceiver));
        intent.SetAction(ActionShow);

        // Extras при сопоставлении PendingIntent не учитываются, поэтому для отмены
        // достаточно совпадения requestCode, компонента, действия и флагов
        var pendingIntent = PendingIntent.GetBroadcast(context, notificationId, intent, BroadcastFlags);
        if (pendingIntent == null) return;

        alarmManager.Cancel(pendingIntent);
        pendingIntent.Cancel();
    }
    #endregion
}
