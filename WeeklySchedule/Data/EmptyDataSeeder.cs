using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Services;

namespace WeeklySchedule.Data;

// Обычный запуск не добавляет демонстрационные пары. MainViewModel создаст
// только пустой таймлайн, если пользователь ещё не создал ни одного.
public sealed class EmptyDataSeeder : IDataSeeder
{
    public Task SeedAsync(ILessonRepository lessons, ITimelineRepository timelines,
        IActiveScheduleService active) => Task.CompletedTask;
}
