using Microsoft.Extensions.Logging;
using System.Text.Json;
using WeeklySchedule.Models;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Data.Repositories;

public class FileLessonRepository : ILessonRepository
{
    private readonly string _baseDirectoryPath;
    private readonly ILogger<FileLessonRepository>? _logger;
    private readonly Lock _lock = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Логгер необязателен: репозиторий создают и вне контейнера
    public FileLessonRepository(ILogger<FileLessonRepository>? logger = null)
    {
        _baseDirectoryPath = Path.Combine(FileSystem.AppDataDirectory, "Timelines");
        _logger = logger;
    }

    private string GetDirectoryPath(Guid timelineId) =>
        Path.Combine(_baseDirectoryPath, timelineId.ToString(), "Lessons");

    private string GetFilePath(Guid timelineId, Guid lessonId) =>
        Path.Combine(GetDirectoryPath(timelineId), $"{lessonId}.json");

    private void EnsureDirectoryExists(Guid timelineId)
    {
        var dir = GetDirectoryPath(timelineId);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }

    // Удаляет файлы пары во всех таймлайнах, кроме keepPath. Вызывать под _lock
    private void DeleteCopiesExcept(Guid lessonId, string keepPath)
    {
        if (!Directory.Exists(_baseDirectoryPath)) return;
        foreach (var timelineDir in Directory.GetDirectories(_baseDirectoryPath))
        {
            var path = Path.Combine(timelineDir, "Lessons", $"{lessonId}.json");
            if (string.Equals(path, keepPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// Читает пары из папки, пропуская нечитаемые файлы, но называя каждый из них.
    /// Раньше здесь стоял пустой catch: файл, который не открылся, исчезал из недели
    /// молча — расписание оставалось в списке, пары из него пропадали, и сказать,
    /// что именно не прочиталось, было нечем. Вызывать под _lock.
    /// </summary>
    private void ReadLessonsInto(string lessonsDir, List<Lesson> lessons, bool requireComplete = false)
    {
        int unreadable = 0;
        foreach (var file in Directory.GetFiles(lessonsDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var lesson = JsonSerializer.Deserialize<Lesson>(json, _jsonOptions)
                    ?? throw new JsonException("Вместо пары записан null.");
                if (requireComplete && (!Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id)
                    || id != lesson.Id || lesson.TimelineId.ToString() != new DirectoryInfo(lessonsDir).Parent!.Name))
                    throw new JsonException("Идентификаторы пары не соответствуют её файлу.");
                lessons.Add(lesson);
            }
            catch (Exception ex)
            {
                unreadable++;
                _logger?.LogError(ex, "Файл пары {File} не прочитан", file);
                if (requireComplete) throw new IncompleteLessonReadException(file, ex);
            }
        }

        if (unreadable > 0)
            _logger?.LogError("Не прочитано пар в {Directory}: {Unreadable}", lessonsDir, unreadable);
    }

    public async Task<IEnumerable<Lesson>> GetAllAsync()
    {
        var lessons = new List<Lesson>();
        await Task.Run(() =>
        {
            lock (_lock)
            {
                if (!Directory.Exists(_baseDirectoryPath)) return;
                foreach (var timelineDir in Directory.GetDirectories(_baseDirectoryPath))
                {
                    var lessonsDir = Path.Combine(timelineDir, "Lessons");
                    if (Directory.Exists(lessonsDir)) ReadLessonsInto(lessonsDir, lessons);
                }
            }
        });
        return lessons;
    }

    public Task<IEnumerable<Lesson>> GetByTimelineIdAsync(Guid timelineId) => ReadTimelineAsync(timelineId, false);

    public Task<IEnumerable<Lesson>> GetByTimelineIdForReplacementAsync(Guid timelineId) => ReadTimelineAsync(timelineId, true);

    private async Task<IEnumerable<Lesson>> ReadTimelineAsync(Guid timelineId, bool requireComplete)
    {
        var lessons = new List<Lesson>();
        await Task.Run(() =>
        {
            lock (_lock)
            {
                var dir = GetDirectoryPath(timelineId);
                // В отличие от Directory.Exists перечисление не маскирует отказ в доступе
                // под пустое расписание. Отсутствие каталога допустимо при первом импорте.
                if (requireComplete)
                {
                    try { ReadLessonsInto(dir, lessons, true); }
                    catch (DirectoryNotFoundException) { }
                }
                else if (Directory.Exists(dir)) ReadLessonsInto(dir, lessons);
            }
        });
        return lessons;
    }

    public async Task<Lesson?> GetByIdAsync(Guid id)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(l => l.Id == id);
    }

    /// <summary>
    /// Один файл по известному пути. Тем же занимался <see cref="GetByIdAsync(Guid)"/>,
    /// но через обход и разбор всех файлов всех расписаний: нажатие на карточку не
    /// открывало пару, пока не будет прочитано все хранилище.
    /// </summary>
    public async Task<Lesson?> GetByIdAsync(Guid timelineId, Guid id)
    {
        Lesson? found = null;
        await Task.Run(() =>
        {
            lock (_lock)
            {
                var path = GetFilePath(timelineId, id);
                if (!File.Exists(path)) return;
                try
                {
                    found = JsonSerializer.Deserialize<Lesson>(File.ReadAllText(path), _jsonOptions);
                }
                catch (Exception ex)
                {
                    // Не прочиталось — вызывающий пойдет искать полным обходом, и там
                    // этот же файл будет пропущен уже с подробностями
                    _logger?.LogError(ex, "Файл пары {File} не прочитан", path);
                }
            }
        });
        return found;
    }

    public async Task AddAsync(Lesson lesson)
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                EnsureDirectoryExists(lesson.TimelineId);
                AtomicFile.WriteAllText(GetFilePath(lesson.TimelineId, lesson.Id), JsonSerializer.Serialize(lesson, _jsonOptions));
            }
        });
    }

    public async Task UpdateAsync(Lesson lesson)
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                EnsureDirectoryExists(lesson.TimelineId);
                var newPath = GetFilePath(lesson.TimelineId, lesson.Id);
                AtomicFile.WriteAllText(newPath, JsonSerializer.Serialize(lesson, _jsonOptions));

                // Пару могли перенести в другой таймлайн: файл в старой папке
                // остался бы и читался как дубликат в GetAllAsync
                DeleteCopiesExcept(lesson.Id, newPath);
            }
        });
    }

    public async Task DeleteAsync(Guid id)
    {
        var lesson = await GetByIdAsync(id);
        if (lesson != null)
        {
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    var path = GetFilePath(lesson.TimelineId, id);
                    if (File.Exists(path)) File.Delete(path);
                }
            });
        }
    }

    // Таймлайн известен заранее, поэтому обходить хранилище не нужно. Через
    // DeleteAsync повторный разбор сделал бы полный обход папок на каждую пару.
    public async Task DeleteManyAsync(Guid timelineId, IEnumerable<Guid> ids)
    {
        var list = ids.ToList();
        if (list.Count == 0) return;
        await Task.Run(() =>
        {
            lock (_lock)
            {
                foreach (var id in list)
                {
                    var path = GetFilePath(timelineId, id);
                    if (File.Exists(path)) File.Delete(path);
                }
            }
        });
    }
}
