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
    NoLessons,      // группа есть, но пар не нашлось: похоже на поломку разбора
    IncompleteParse // известные ошибки разбора: заменять прежние пары нельзя
}

/// <param name="Parsed">Пар нашлось в файле.</param>
/// <param name="Stored">Пар записано: разбор мог описать один слот дважды.</param>
/// <param name="Removed">Прежних пар удалено, включая заведенные вручную.</param>
/// <param name="Skipped">Строки файла, у которых не удалось определить время.</param>
public sealed record ReimportResult(ReimportStatus Status, int Parsed = 0, int Stored = 0,
    int Removed = 0, int Skipped = 0);

/// <summary>
/// Повторный разбор сохраненного xlsx. Расписание после него совпадает с файлом:
/// прежнее содержимое уходит целиком, включая пары, заведенные руками. Раньше
/// уцелевшие пары вставали рядом с новыми, и каждое перечитывание удваивало неделю.
/// </summary>
public static class ScheduleReimportService
{
    public static async Task<ReimportResult> ReimportAsync(
        ILessonRepository lessonRepo,
        ITimelineRepository timelineRepo,
        Timeline timeline,
        ILogger<ExcelMIPTScheduleParser> logger,
        Action? onMutation = null)
    {
        var source = timeline.Source;
        if (source == null || !ScheduleSourceStore.Exists(timeline.Id, source))
            return new ReimportResult(ReimportStatus.NoSource);

        var path = ScheduleSourceStore.PathFor(timeline.Id, source);
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
        if (parsed.Skipped > 0)
            return new ReimportResult(ReimportStatus.IncompleteParse, Parsed: parsed.Lessons.Count, Skipped: parsed.Skipped);
        // Пустой разбор при существующей группе — тоже повод остановиться, а не
        // удалять все пары: настоящее пустое расписание встречается куда реже
        // поломки формата
        if (parsed.Lessons.Count == 0)
            return new ReimportResult(ReimportStatus.NoLessons, Skipped: parsed.Skipped);

        var (stored, removed) = await LessonImportService.ReplaceAllAsync(
            lessonRepo, timeline.Id, parsed.Lessons, onMutation);

        // Объект редактора меняется только после успешной записи метаданных.
        var updated = new Timeline
        {
            Id = timeline.Id, Name = timeline.Name, NotificationsEnabled = timeline.NotificationsEnabled,
            BaseDays = parsed.BaseDays,
            Source = new ImportSource
            {
                FileName = source.FileName, StoredFileName = source.StoredFileName,
                GroupName = source.GroupName, ImportedAt = DateTime.Now
            }
        };
        onMutation?.Invoke();
        await timelineRepo.UpdateAsync(updated);
        timeline.BaseDays = updated.BaseDays;
        timeline.Source = updated.Source;

        return new ReimportResult(ReimportStatus.Success, parsed.Lessons.Count, stored, removed,
            parsed.Skipped);
    }
}
