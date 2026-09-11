using System.Text.Json;
using WeeklySchedule.Models;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Data.Repositories;

public class FileTimelineRepository : ITimelineRepository
{
    private readonly string _filePath;
    private readonly string _baseDirectoryPath;
    private readonly Lock _lock = new();
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public FileTimelineRepository() { _filePath = Path.Combine(FileSystem.AppDataDirectory, "timelines.json"); _baseDirectoryPath = Path.Combine(FileSystem.AppDataDirectory, "Timelines"); }
    private List<Timeline> LoadAll() { try { var json = File.ReadAllText(_filePath); return JsonSerializer.Deserialize<List<Timeline>>(json, _jsonOptions) ?? throw new JsonException(); } catch (FileNotFoundException) { return []; } catch (DirectoryNotFoundException) { return []; } }
    private List<Timeline> LoadAllForWrite() { try { return LoadAll(); } catch (JsonException) { var backup = Path.Combine(FileSystem.AppDataDirectory, $"timelines.corrupted-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"); File.Copy(_filePath, backup, overwrite: false); return []; } }
    private void SaveAll(List<Timeline> timelines) { AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(timelines, _jsonOptions)); }

    public async Task<IEnumerable<Timeline>> GetAllAsync(CancellationToken ct = default) { return await Task.Run(() => { lock (_lock) return LoadAll(); }, ct); }
    public async Task<Timeline?> GetByIdAsync(Guid id, CancellationToken ct = default) { return await Task.Run(() => { lock (_lock) return LoadAll().FirstOrDefault(t => t.Id == id); }, ct); }
    public async Task AddAsync(Timeline timeline, CancellationToken ct = default) { await Task.Run(() => { lock (_lock) { var list = LoadAllForWrite(); if (list.Any(t => t.Id == timeline.Id)) return; list.Add(timeline); SaveAll(list); Directory.CreateDirectory(Path.Combine(_baseDirectoryPath, timeline.Id.ToString())); } }, ct); }
    public async Task UpdateAsync(Timeline timeline, CancellationToken ct = default) { await Task.Run(() => { lock (_lock) { var list = LoadAllForWrite(); var index = list.FindIndex(t => t.Id == timeline.Id); if (index != -1) { list[index] = timeline; SaveAll(list); } } }, ct); }
    public async Task DeleteAsync(Guid id, CancellationToken ct = default) { await Task.Run(() => { lock (_lock) { var list = LoadAllForWrite(); list.RemoveAll(t => t.Id == id); SaveAll(list); var timelineDir = Path.Combine(_baseDirectoryPath, id.ToString()); if (Directory.Exists(timelineDir)) Directory.Delete(timelineDir, true); } }, ct); }
}