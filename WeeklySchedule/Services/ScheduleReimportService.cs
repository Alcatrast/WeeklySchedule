using Microsoft.Extensions.Logging;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Extensions;
using WeeklySchedule.Models;

namespace WeeklySchedule.Services;

public enum ReimportStatus
{
    Success,
    NoSource,       // исходник не сохранен или пропал с диска
    GroupNotFound,  // группы больше нет в файле — формат или название изменились
    NoLessons       // группа есть, но пар не нашлось: похоже на поломку разбора
}

/// <param name="Repaired">Невидимые пары от прежних разборов, убранные заодно.</param>
/// <param name="Skipped">Строки файла, у которых не удалось определить время.</param>
public sealed record ReimportResult(ReimportStatus Status, int Parsed = 0, int Added = 0,
    int Removed = 0, int Repaired = 0, int Skipped = 0);

/// <summary>
/// Повторный разбор сохраненного xlsx. Сносит только пары с <see cref="Lesson.FromImport"/>:
/// заведенные руками переживают обновление расписания.
/// </summary>
public static class ScheduleReimportService
{
    public static async Task<ReimportResult> ReimportAsync(
        ILessonRepository lessonRepo,
        ITimelineRepository timelineRepo,
        Timeline timeline,
        ILogger<ExcelMIPTScheduleParser> logger)
    {
        var source = timeline.Source;
        if (source == null || !ScheduleSourceStore.Exists(timeline.Id))
            return new ReimportResult(ReimportStatus.NoSource);

        var path = ScheduleSourceStore.PathFor(timeline.Id);
        var parsed = await Task.Run(() =>
        {
            var parser = new ExcelMIPTScheduleParser(logger);
            // ParseGroupSchedule на ненайденной группе молча возвращает пустой список,
            // и повторный разбор вычистил бы расписание, ничего не сказав. Наличие
            // группы проверяем отдельно, до разбора
            var groups = parser.ExtractAllGroupNames(path);
            if (!groups.Contains(source.GroupName, StringComparer.OrdinalIgnoreCase))
                return (Found: false, Lessons: new List<Lesson>(), BaseDays: new List<BaseDay>(), Skipped: 0);

            var lessons = parser.ParseGroupSchedule(path, source.GroupName, out var baseDays, out int skipped);
            return (Found: true, Lessons: lessons, BaseDays: baseDays, Skipped: skipped);
        });

        if (!parsed.Found) return new ReimportResult(ReimportStatus.GroupNotFound);
        // Пустой разбор при существующей группе — тоже повод остановиться, а не
        // удалять все пары: настоящее пустое расписание встречается куда реже
        // поломки формата
        if (parsed.Lessons.Count == 0)
            return new ReimportResult(ReimportStatus.NoLessons, Skipped: parsed.Skipped);

        var existing = (await lessonRepo.GetByTimelineIdAsync(timeline.Id)).ToList();
        var stale = existing.Where(l => l.FromImport).Select(l => l.Id).ToList();
        await lessonRepo.DeleteManyAsync(timeline.Id, stale);

        // Пары без признака импорта, оставшиеся невидимыми от прежних разборов:
        // ручными они не являются, а перечитывание — единственный момент, когда
        // есть чем их заменить
        int repaired = await LessonImportService.RemoveUnrenderableAsync(lessonRepo, timeline.Id);

        foreach (var lesson in parsed.Lessons) lesson.FromImport = true;
        // AddMissingAsync сверяется с тем, что осталось, то есть с ручными парами:
        // совпавшую по содержимому пару он не задвоит
        int added = await LessonImportService.AddMissingAsync(lessonRepo, timeline.Id, parsed.Lessons);

        // Пометки заменяем, а не копим: устаревший базовый день оставался бы навсегда
        timeline.BaseDays = parsed.BaseDays;
        source.ImportedAt = DateTime.Now;
        await timelineRepo.UpdateAsync(timeline);

        return new ReimportResult(ReimportStatus.Success, parsed.Lessons.Count, added, stale.Count,
            repaired, parsed.Skipped);
    }
}
