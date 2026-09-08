using WeeklySchedule.Utilities;
using WeeklySchedule.Models;

namespace WeeklySchedule.Services;

/// <summary>
/// Копия исходного xlsx рядом с парами таймлайна.
///
/// Системный пикер отдает путь во временную папку: на Android это копия content-URI
/// в кэше приложения, и ОС вправе ее вычистить. Разбор шел прямо по этому пути и
/// повторялся при каждом появлении экрана выбора группы, поэтому возврат из фона
/// после чистки кэша давал «Ошибка при чтении файла». Своя копия в приватной папке
/// не исчезает и заодно позволяет повторить разбор кнопкой.
/// </summary>
public static class ScheduleSourceStore
{
    private const string FileName = "source.xlsx";

    private static string DirectoryFor(Guid timelineId) =>
        Path.Combine(FileSystem.AppDataDirectory, "Timelines", timelineId.ToString());

    // Внутри папки таймлайна намеренно: FileTimelineRepository.DeleteAsync сносит ее
    // рекурсивно, значит исходник убирается вместе с расписанием сам собой
    public static string PathFor(Guid timelineId, ImportSource? source = null)
    {
        var name = source?.StoredFileName ?? FileName;
        if (name != FileName && !IsStagedName(name))
            throw new InvalidDataException("Некорректное имя сохранённого исходника.");
        return Path.Combine(DirectoryFor(timelineId), name);
    }

    public static bool Exists(Guid timelineId, ImportSource? source = null) => File.Exists(PathFor(timelineId, source));

    // Копия становится действующей только после записи её имени в Timeline.Source.
    // Отмена и неудачный разбор не затрагивают предыдущий исходник.
    public static async Task<string> StageAsync(Guid timelineId, Stream contents)
    {
        var directory = DirectoryFor(timelineId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"source-{Guid.NewGuid():N}.xlsx");
        await Task.Run(() => AtomicFile.WriteAllStream(path, contents));
        return path;
    }

    private static bool IsStagedName(string name) =>
        name.StartsWith("source-", StringComparison.Ordinal) && name.EndsWith(".xlsx", StringComparison.Ordinal)
        && name.Length == 44 && Guid.TryParseExact(name.Substring(7, 32), "N", out _);

    public static async Task<string> SaveAsync(Guid timelineId, Stream contents)
    {
        var path = PathFor(timelineId);
        Directory.CreateDirectory(DirectoryFor(timelineId));
        await Task.Run(() => AtomicFile.WriteAllStream(path, contents));
        return path;
    }

    /// <summary>
    /// Убирает только копию конкретной попытки. Каталог с парами никогда не удаляется.
    /// </summary>
    public static void DiscardPending(Guid timelineId, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsStagedName(Path.GetFileName(fullPath)) ||
            !string.Equals(Path.GetDirectoryName(fullPath), Path.GetFullPath(DirectoryFor(timelineId)),
                StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            File.Delete(fullPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
