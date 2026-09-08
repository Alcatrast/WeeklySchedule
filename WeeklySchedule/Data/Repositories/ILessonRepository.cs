using WeeklySchedule.Models;

namespace WeeklySchedule.Data.Repositories;

public interface ILessonRepository
{
    Task<IEnumerable<Lesson>> GetAllAsync();
    Task<IEnumerable<Lesson>> GetByTimelineIdAsync(Guid timelineId);
    /// <summary>
    /// Полный набор перед заменой. Реализации с пропуском ошибок при обычном чтении
    /// должны переопределить этот метод и прерывать его при любой непрочитанной записи.
    /// </summary>
    Task<IEnumerable<Lesson>> GetByTimelineIdForReplacementAsync(Guid timelineId) =>
        GetByTimelineIdAsync(timelineId);
    Task<Lesson?> GetByIdAsync(Guid id);
    Task AddAsync(Lesson lesson);
    Task UpdateAsync(Lesson lesson);
    Task DeleteAsync(Guid id);

    /// <summary>Удаляет пары одного таймлайна пачкой — для повторного разбора xlsx.</summary>
    Task DeleteManyAsync(Guid timelineId, IEnumerable<Guid> ids);
}
