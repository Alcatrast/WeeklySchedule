using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Services;

public static class LessonImportService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Сносит пары, которые невозможно показать: конец не позже начала. Такие оставлял
    /// разбор xlsx, когда распознавал только одну границу времени. В хранилище они
    /// лежали и даже поднимали уведомления, а WeekLayout их отбрасывает — день выглядел
    /// пустым. Сами по себе они бы не ушли: у них нет признака импорта, а разбор давал
    /// тот же испорченный ключ, и <see cref="AddMissingAsync"/> считал пару уже добавленной.
    /// Вызывается на обоих путях импорта, до добавления разобранных пар.
    /// </summary>
    public static async Task<int> RemoveUnrenderableAsync(ILessonRepository repository, Guid timelineId)
    {
        var broken = (await repository.GetByTimelineIdAsync(timelineId))
            .Where(l => !LessonTimeRange.IsValid(l.StartTime, l.EndTime))
            .Select(l => l.Id).ToList();
        if (broken.Count == 0) return 0;
        await repository.DeleteManyAsync(timelineId, broken);
        return broken.Count;
    }

    // Сравниваем все содержательные поля, а не случайный Id из нового разбора Excel.
    // Разные преподаватели, описания и типы в одно время остаются отдельными парами.
    private static object Key(Lesson lesson) =>
        (lesson.Day, lesson.StartTime, lesson.EndTime, lesson.Name, lesson.Description, lesson.Type);

    public static async Task<int> AddMissingAsync(ILessonRepository repository, Guid timelineId,
        IEnumerable<Lesson> lessons)
    {
        await Gate.WaitAsync();
        try
        {
            var existing = (await repository.GetByTimelineIdAsync(timelineId)).Select(Key).ToHashSet();
            int added = 0;
            foreach (var lesson in lessons)
            {
                if (!existing.Add(Key(lesson))) continue;
                lesson.TimelineId = timelineId;
                await repository.AddAsync(lesson);
                added++;
            }
            return added;
        }
        finally { Gate.Release(); }
    }
}
