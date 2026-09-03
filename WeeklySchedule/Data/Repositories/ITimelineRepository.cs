using WeeklySchedule.Models;

namespace WeeklySchedule.Data.Repositories;

public interface ITimelineRepository
{
    Task<IEnumerable<Timeline>> GetAllAsync();
    Task<Timeline?> GetByIdAsync(Guid id);
    Task AddAsync(Timeline timeline);
    Task UpdateAsync(Timeline timeline);
    Task DeleteAsync(Guid id);
}