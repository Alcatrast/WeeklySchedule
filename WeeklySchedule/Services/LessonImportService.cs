using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;

namespace WeeklySchedule.Services;

public static class LessonImportService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

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
