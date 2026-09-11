using WeeklySchedule.Models;

namespace WeeklySchedule.Data.Repositories;

public interface ILessonRepository
{
    Task<IEnumerable<Lesson>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<Lesson>> GetByTimelineIdAsync(Guid timelineId, CancellationToken ct = default);
    Task<Lesson?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Lesson lesson, CancellationToken ct = default);
    Task UpdateAsync(Lesson lesson, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}