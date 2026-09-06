using WeeklySchedule.Utilities;

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
    public static string PathFor(Guid timelineId) =>
        Path.Combine(DirectoryFor(timelineId), FileName);

    public static bool Exists(Guid timelineId) => File.Exists(PathFor(timelineId));

    public static async Task<string> SaveAsync(Guid timelineId, Stream contents)
    {
        var path = PathFor(timelineId);
        Directory.CreateDirectory(DirectoryFor(timelineId));
        await Task.Run(() => AtomicFile.WriteAllStream(path, contents));
        return path;
    }

    /// <summary>
    /// Убирает копию, оставшуюся от незавершенного импорта: в режиме создания
    /// таймлайн попадает в репозиторий только после выбора группы, и если
    /// пользователь ушел раньше, удалять папку будет уже некому.
    /// </summary>
    public static void DiscardOrphan(Guid timelineId)
    {
        try
        {
            var directory = DirectoryFor(timelineId);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
