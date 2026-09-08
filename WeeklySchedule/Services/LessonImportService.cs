using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;

namespace WeeklySchedule.Services;

public static class LessonImportService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    // Одна пара приезжает из разбора дважды, когда соседние колонки группы делят
    // объединенную ячейку. Сравниваем содержимое, а не случайный Id из разбора
    private static object Key(Lesson lesson) =>
        (lesson.Day, lesson.StartTime, lesson.EndTime, lesson.Name, lesson.Description, lesson.Type);

    /// <summary>
    /// Расписание становится тем, что в файле: разобранные пары записываются, все
    /// прежние удаляются.
    ///
    /// Накопительный импорт двоил расписание при каждом обновлении файла. Уже
    /// существующей считалась пара с точно тем же днем, временем, названием,
    /// описанием и типом, а обновленный разбор дает другие слова — и рядом со
    /// старой парой вставала новая. Отличить пару прежнего импорта от заведенной
    /// руками нечем, поэтому замена полная: так решено 07.09.2026.
    ///
    /// Порядок шагов — часть смысла. Сначала пишутся новые пары и только потом
    /// удаляются прежние: обрыв посередине (исключение, снятие процесса Android'ом)
    /// оставит расписание с лишними парами, а не пустым. Лишнее видно и чинится
    /// повторным импортом, пустое расписание неотличимо от пропажи данных.
    /// </summary>
    /// <returns>Сколько пар записано и сколько прежних удалено.</returns>
    public static async Task<(int Stored, int Removed)> ReplaceAllAsync(ILessonRepository repository,
        Guid timelineId, IEnumerable<Lesson> lessons, Action? onMutation = null)
    {
        await Gate.WaitAsync();
        try
        {
            // Прежние пары запоминаем по Id до записи: удалить нужно ровно их, а не
            // все, что окажется в папке после добавления новых
            var stale = (await repository.GetByTimelineIdForReplacementAsync(timelineId)).Select(l => l.Id).ToList();

            var written = new HashSet<object>();
            int stored = 0;
            foreach (var lesson in lessons)
            {
                if (!written.Add(Key(lesson))) continue;
                // Даже бросившая запись могла успеть изменить файл. Вызывающий код
                // должен перечитать данные при ошибке, а не оставить старый кэш.
                onMutation?.Invoke();
                lesson.TimelineId = timelineId;
                await repository.AddAsync(lesson);
                stored++;
            }

            if (stale.Count > 0)
            {
                onMutation?.Invoke();
                await repository.DeleteManyAsync(timelineId, stale);
            }
            return (stored, stale.Count);
        }
        finally { Gate.Release(); }
    }
}
