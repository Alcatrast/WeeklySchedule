using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Messaging;
using WeeklySchedule.Models;

namespace WeeklySchedule.Services;

// Одинаковое подтверждение и запись для редактора, просмотра и контекстного меню.
public sealed class ItemDeletionService(ILessonRepository lessons, ITimelineRepository timelines,
    ISettingsService settings)
{
    private int _busy;

    public async Task<bool> DeleteLessonAsync(Lesson lesson, Func<string, string, Task<bool>> confirm)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) return false;
        try
        {
            if (!await confirm("Удаление пары", $"Удалить пару «{lesson.Name}»?")) return false;
            await lessons.DeleteAsync(lesson.Id);
            AppEvents.NotifyDataChanged(lesson.Day);
            return true;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    public async Task<bool> DeleteTimelineAsync(Timeline timeline, Func<string, string, Task<bool>> confirm)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) return false;
        try
        {
            if (!await confirm("Удаление таймлайна", $"Удалить таймлайн «{timeline.Name}» и все его пары?")) return false;
            await timelines.DeleteAsync(timeline.Id);
            if (settings.StartupTimelineId == timeline.Id) settings.StartupTimelineId = Guid.Empty;
            AppEvents.NotifyDataChanged();
            return true;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }
}
