using WeeklySchedule.Models;

namespace WeeklySchedule.Data.Repositories;

public class InMemoryLessonRepository : ILessonRepository
{
    private readonly List<Lesson> _lessons = [];
    private readonly Lock _lock = new();

    public Task<IEnumerable<Lesson>> GetAllAsync()
    {
        lock (_lock) return Task.FromResult<IEnumerable<Lesson>>([.. _lessons]);
    }

    public Task<IEnumerable<Lesson>> GetByTimelineIdAsync(Guid timelineId)
    {
        lock (_lock) return Task.FromResult<IEnumerable<Lesson>>([.. _lessons.Where(l => l.TimelineId == timelineId)]);
    }

    public Task<Lesson?> GetByIdAsync(Guid id)
    {
        lock (_lock) return Task.FromResult(_lessons.FirstOrDefault(l => l.Id == id));
    }

    public Task AddAsync(Lesson lesson)
    {
        lock (_lock) _lessons.Add(lesson);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Lesson lesson)
    {
        lock (_lock)
        {
            var i = _lessons.FindIndex(l => l.Id == lesson.Id);
            if (i != -1) _lessons[i] = lesson;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        lock (_lock) _lessons.RemoveAll(l => l.Id == id);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        lock (_lock) _lessons.Clear();
        return Task.CompletedTask;
    }
}