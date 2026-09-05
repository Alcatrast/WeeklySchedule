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

    // null означает "файл есть, но прочитать его не удалось". Отличать этот случай
    // от пустого списка обязательно: иначе любая запись поверх стирает данные,
    // которые не смогли прочитать из-за временной ошибки ввода-вывода
    private List<Timeline>? TryLoadAll()
    {
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<Timeline>>(json, _jsonOptions) ?? [];
        }
        catch { return null; }
    }

    private List<Timeline> LoadAll() => TryLoadAll() ?? [];

    // Список для операции, которая потом вызовет SaveAll. Нечитаемый файл сначала
    // уводим в резервную копию, чтобы данные можно было достать руками
    private List<Timeline> LoadAllForWrite()
    {
        var list = TryLoadAll();
        if (list != null) return list;

        try
        {
            if (File.Exists(_filePath))
            {
                var backup = Path.Combine(
                    FileSystem.AppDataDirectory,
                    $"timelines.corrupted-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Move(_filePath, backup, overwrite: true);
            }
        }
        catch { }

        return [];
    }

    private void SaveAll(List<Timeline> timelines)
    {
        // Пишем через временный файл: обрыв записи не оставит обрезанный timelines.json
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(timelines, _jsonOptions));
        File.Move(tempPath, _filePath, overwrite: true);
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
                var list = LoadAllForWrite();
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
                var list = LoadAllForWrite();
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
                var list = LoadAllForWrite();
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