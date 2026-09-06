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

    /// <summary>Удаляет пары одного таймлайна пачкой — для повторного разбора xlsx.</summary>
    Task DeleteManyAsync(Guid timelineId, IEnumerable<Guid> ids);
}