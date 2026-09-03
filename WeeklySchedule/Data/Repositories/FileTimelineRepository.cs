using System.Text.Json;
using WeeklySchedule.Models;

namespace WeeklySchedule.Data.Repositories;

public class FileTimelineRepository : ITimelineRepository
{
    private readonly string _filePath;
    private readonly string _baseDirectoryPath;
    private readonly Lock _lock = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FileTimelineRepository()
    {
        _filePath = Path.Combine(FileSystem.AppDataDirectory, "timelines.json");
        _baseDirectoryPath = Path.Combine(FileSystem.AppDataDirectory, "Timelines");

        if (!File.Exists(_filePath)) File.WriteAllText(_filePath, "[]");
    }

    private List<Timeline> LoadAll()
    {
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<Timeline>>(json, _jsonOptions) ?? [];
        }
        catch { return []; }
    }

    private void SaveAll(List<Timeline> timelines)
    {
        File.WriteAllText(_filePath, JsonSerializer.Serialize(timelines, _jsonOptions));
    }

    public async Task<IEnumerable<Timeline>> GetAllAsync()
    {
        return await Task.Run(() => { lock (_lock) return LoadAll(); });
    }

    public async Task<Timeline?> GetByIdAsync(Guid id)
    {
        return await Task.Run(() => { lock (_lock) return LoadAll().FirstOrDefault(t => t.Id == id); });
    }

    public async Task AddAsync(Timeline timeline)
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                var list = LoadAll();
                list.Add(timeline);
                SaveAll(list);
                Directory.CreateDirectory(Path.Combine(_baseDirectoryPath, timeline.Id.ToString()));
            }
        });
    }

    public async Task UpdateAsync(Timeline timeline)
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                var list = LoadAll();
                var index = list.FindIndex(t => t.Id == timeline.Id);
                if (index != -1)
                {
                    list[index] = timeline;
                    SaveAll(list);
                }
            }
        });
    }

    public async Task DeleteAsync(Guid id)
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                var list = LoadAll();
                list.RemoveAll(t => t.Id == id);
                SaveAll(list);
                var timelineDir = Path.Combine(_baseDirectoryPath, id.ToString());
                if (Directory.Exists(timelineDir))
                {
                    Directory.Delete(timelineDir, true);
                }
            }
        });
    }
}