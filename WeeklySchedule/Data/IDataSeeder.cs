using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Services;

namespace WeeklySchedule.Data;

public interface IDataSeeder
{
    Task SeedAsync(ILessonRepository lessonRepo, ITimelineRepository timelineRepo, IActiveScheduleService scheduleService);
}