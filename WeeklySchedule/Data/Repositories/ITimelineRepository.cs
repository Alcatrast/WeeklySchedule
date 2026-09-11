using WeeklySchedule.Models;

namespace WeeklySchedule.Data.Repositories;

public interface ITimelineRepository
{
    Task<IEnumerable<Timeline>> GetAllAsync(CancellationToken ct = default);
    Task<Timeline?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Timeline timeline, CancellationToken ct = default);
    Task UpdateAsync(Timeline timeline, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}