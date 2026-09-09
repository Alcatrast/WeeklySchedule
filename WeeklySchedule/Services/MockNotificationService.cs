using System.Diagnostics;

namespace WeeklySchedule.Services;

public class MockNotificationService : INotificationService
{
    public Task<bool> CheckPermissionAsync() => Task.FromResult(true);
    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
    public Task<bool> OpenNotificationSettingsAsync() => Task.FromResult(true);

    public Task<bool> CanScheduleExactAlarmsAsync() => Task.FromResult(true);
    public Task<bool> RequestExactAlarmsAsync() => Task.FromResult(true);

    public Task ReplaceScheduledAsync(IReadOnlyList<PlannedNotification> plan)
    {
#if DEBUG
        Debug.WriteLine($"[MOCK Notification] Запланировано напоминаний: {plan.Count}");
        foreach (var item in plan)
            Debug.WriteLine($"[MOCK Notification]   '{item.Title}' на {item.Day} " +
                $"{item.StartTime:hh\\:mm} (за {item.MinutesBefore} мин.)");
#endif
        return Task.CompletedTask;
    }
}
