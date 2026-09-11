using System.Text.Json;
using WeeklySchedule.Models;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Data.Repositories;

public class FileLessonRepository : ILessonRepository
{
    private readonly string _baseDirectoryPath;
    private readonly Lock _lock = new();
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public FileLessonRepository() { _baseDirectoryPath = Path.Combine(FileSystem.AppDataDirectory, "Timelines"); }
    private string GetDirectoryPath(Guid timelineId) => Path.Combine(_baseDirectoryPath, timelineId.ToString(), "Lessons");
    private string GetFilePath(Guid timelineId, Guid lessonId) => Path.Combine(GetDirectoryPath(timelineId), $"{lessonId}.json");
    private void EnsureDirectoryExists(Guid timelineId) { var dir = GetDirectoryPath(timelineId); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
    private void DeleteCopiesExcept(Guid lessonId, string keepPath) { if (!Directory.Exists(_baseDirectoryPath)) return; foreach (var timelineDir in Directory.GetDirectories(_baseDirectoryPath)) { var path = Path.Combine(timelineDir, "Lessons", $"{lessonId}.json"); if (string.Equals(path, keepPath, StringComparison.OrdinalIgnoreCase)) continue; if (File.Exists(path)) File.Delete(path); } }

    public async Task<IEnumerable<Lesson>> GetAllAsync(CancellationToken ct = default)
    {
        var lessons = new List<Lesson>();
        await Task.Run(() => { lock (_lock) { if (!Directory.Exists(_baseDirectoryPath)) return; foreach (var timelineDir in Directory.GetDirectories(_baseDirectoryPath)) { var lessonsDir = Path.Combine(timelineDir, "Lessons"); if (Directory.Exists(lessonsDir)) foreach (var file in Directory.GetFiles(lessonsDir, "*.json")) { ct.ThrowIfCancellationRequested(); try { var json = File.ReadAllText(file); var lesson = JsonSerializer.Deserialize<Lesson>(json, _jsonOptions); if (lesson != null) lessons.Add(lesson); } catch { } } } } }, ct);
        return lessons;
    }

    public async Task<IEnumerable<Lesson>> GetByTimelineIdAsync(Guid timelineId, CancellationToken ct = default)
    {
        var lessons = new List<Lesson>();
        await Task.Run(() => { lock (_lock) { var dir = GetDirectoryPath(timelineId); if (!Directory.Exists(dir)) return; foreach (var file in Directory.GetFiles(dir, "*.json")) { ct.ThrowIfCancellationRequested(); try { var json = File.ReadAllText(file); var lesson = JsonSerializer.Deserialize<Lesson>(json, _jsonOptions); if (lesson != null) lessons.Add(lesson); } catch { } } } }, ct);
        return lessons;
    }

    public async Task<Lesson?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            lock (_lock)
            {
                if (!Directory.Exists(_baseDirectoryPath)) return null;
                var files = Directory.GetFiles(_baseDirectoryPath, $"{id}.json", SearchOption.AllDirectories);
                if (files.Length == 0) return null;
                var json = File.ReadAllText(files[0]);
                return JsonSerializer.Deserialize<Lesson>(json, _jsonOptions);
            }
        }, ct);
    }

    public async Task AddAsync(Lesson lesson, CancellationToken ct = default) { await Task.Run(() => { lock (_lock) { EnsureDirectoryExists(lesson.TimelineId); AtomicFile.WriteAllText(GetFilePath(lesson.TimelineId, lesson.Id), JsonSerializer.Serialize(lesson, _jsonOptions)); } }, ct); }
    public async Task UpdateAsync(Lesson lesson, CancellationToken ct = default) { await Task.Run(() => { lock (_lock) { EnsureDirectoryExists(lesson.TimelineId); var newPath = GetFilePath(lesson.TimelineId, lesson.Id); AtomicFile.WriteAllText(newPath, JsonSerializer.Serialize(lesson, _jsonOptions)); DeleteCopiesExcept(lesson.Id, newPath); } }, ct); }
    public async Task DeleteAsync(Guid id, CancellationToken ct = default) { var lesson = await GetByIdAsync(id, ct); if (lesson != null) await Task.Run(() => { lock (_lock) { var path = GetFilePath(lesson.TimelineId, id); if (File.Exists(path)) File.Delete(path); } }, ct); }
}