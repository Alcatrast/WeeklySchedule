using WeeklySchedule.Models;

namespace WeeklySchedule.Data.Repositories;

public interface ILessonRepository
{
    Task<IEnumerable<Lesson>> GetAllAsync();
    Task<IEnumerable<Lesson>> GetByTimelineIdAsync(Guid timelineId);
    Task<Lesson?> GetByIdAsync(Guid id);
    Task AddAsync(Lesson lesson);
    Task UpdateAsync(Lesson lesson);
    Task DeleteAsync(Guid id);
    Task ClearAsync();
}