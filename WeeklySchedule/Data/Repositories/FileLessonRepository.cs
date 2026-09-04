using System.Text.Json;
using WeeklySchedule.Models;

namespace WeeklySchedule.Data.Repositories;

public class FileLessonRepository : ILessonRepository
{
    private readonly string _baseDirectoryPath;
    private readonly Lock _lock = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FileLessonRepository()
    {
        _baseDirectoryPath = Path.Combine(FileSystem.AppDataDirectory, "Timelines");
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
                    if (Directory.Exists(lessonsDir))
                    {
                        foreach (var file in Directory.GetFiles(lessonsDir, "*.json"))
                        {
                            try
                            {
                                var json = File.ReadAllText(file);
                                var lesson = JsonSerializer.Deserialize<Lesson>(json, _jsonOptions);
                                if (lesson != null) lessons.Add(lesson);
                            }
                            catch { }
                        }
                    }
                }
            }
        });
        return lessons;
    }

    public async Task<IEnumerable<Lesson>> GetByTimelineIdAsync(Guid timelineId)
    {
        var lessons = new List<Lesson>();
        await Task.Run(() =>
        {
            lock (_lock)
            {
                var dir = GetDirectoryPath(timelineId);
                if (!Directory.Exists(dir)) return;
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var lesson = JsonSerializer.Deserialize<Lesson>(json, _jsonOptions);
                        if (lesson != null) lessons.Add(lesson);
                    }
                    catch { }
                }
            }
        });
        return lessons;
    }

    public async Task<Lesson?> GetByIdAsync(Guid id)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(l => l.Id == id);
    }

    public async Task AddAsync(Lesson lesson)
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                EnsureDirectoryExists(lesson.TimelineId);
                File.WriteAllText(GetFilePath(lesson.TimelineId, lesson.Id), JsonSerializer.Serialize(lesson, _jsonOptions));
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
                File.WriteAllText(newPath, JsonSerializer.Serialize(lesson, _jsonOptions));

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

    public async Task ClearAsync()
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                if (Directory.Exists(_baseDirectoryPath)) Directory.Delete(_baseDirectoryPath, true);
            }
        });
    }
}