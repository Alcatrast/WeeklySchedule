using System.Text.Json;
using WeeklySchedule.Models;
using WeeklySchedule.Utilities;

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

    }

    // Ошибки доступа/чтения нельзя выдавать за пустой каталог.
    private List<Timeline> LoadAll()
    {
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<Timeline>>(json, _jsonOptions)
                ?? throw new JsonException("Каталог расписаний содержит null вместо списка.");
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    // Список для операции, которая потом вызовет SaveAll. Нечитаемый файл сначала
    // уводим в резервную копию, чтобы данные можно было достать руками
    private List<Timeline> LoadAllForWrite()
    {
        try
        {
            return LoadAll();
        }
        catch (JsonException)
        {
            var backup = Path.Combine(FileSystem.AppDataDirectory,
                $"timelines.corrupted-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            // Ошибка копирования прерывает операцию. Оригинал остается на месте
            // также и при последующем сбое записи нового каталога.
            File.Copy(_filePath, backup, overwrite: false);
            return [];
        }
    }

    private void SaveAll(List<Timeline> timelines)
    {
        // Пишем через временный файл: обрыв записи не оставит обрезанный timelines.json
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(timelines, _jsonOptions));
    }

    public async Task<bool> TryRecoverCorruptedAsync()
    {
        return await Task.Run(() =>
        {
            lock (_lock)
            {
                try
                {
                    LoadAll();
                    return false;
                }
                catch (JsonException) { }

                // Тот же путь, что и у операций записи: оригинал уезжает в
                // резервную копию, на его место встает пустой каталог
                SaveAll(LoadAllForWrite());
                return true;
            }
        });
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
                if (list.Any(t => t.Id == timeline.Id)) return;
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
