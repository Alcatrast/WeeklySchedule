using WeeklySchedule.Models;

namespace WeeklySchedule.Data.Repositories;

public interface ITimelineRepository
{
    Task<IEnumerable<Timeline>> GetAllAsync();
    Task<Timeline?> GetByIdAsync(Guid id);
    Task AddAsync(Timeline timeline);
    Task UpdateAsync(Timeline timeline);
    Task DeleteAsync(Guid id);

    /// <summary>
    /// Уводит нечитаемый каталог в резервную копию и оставляет на его месте пустой
    /// список. Чтения намеренно бросают вместо того, чтобы выдавать сбой за пустой
    /// каталог, поэтому починку нужно запрашивать явно. Возвращает false, если
    /// каталог читается и чинить нечего.
    /// </summary>
    Task<bool> TryRecoverCorruptedAsync();
}
