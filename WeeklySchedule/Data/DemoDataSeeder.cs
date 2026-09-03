using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Services;

namespace WeeklySchedule.Data;

public class DemoDataSeeder : IDataSeeder
{
    public async Task SeedAsync(ILessonRepository lessonRepo, ITimelineRepository timelineRepo, IActiveScheduleService scheduleService)
    {
        var timelines = await timelineRepo.GetAllAsync();
        var lessons = await lessonRepo.GetAllAsync();

        if (timelines.Any() || lessons.Any()) return;

        var t1 = new Timeline { Name = "Осенний семестр" };
        var t2 = new Timeline { Name = "Зимняя сессия" };
        var t3 = new Timeline { Name = "Летняя практика" };

        await timelineRepo.AddAsync(t1);
        await timelineRepo.AddAsync(t2);
        await timelineRepo.AddAsync(t3);

        await GenerateDiverseLessonsAsync(lessonRepo, t1.Id, 10, t1.Name);
        await GenerateDiverseLessonsAsync(lessonRepo, t2.Id, 10, t2.Name);
        await GenerateDiverseLessonsAsync(lessonRepo, t3.Id, 10, t3.Name);

        scheduleService.ActiveTimelineId = t1.Id;
    }

    private static async Task GenerateDiverseLessonsAsync(ILessonRepository repo, Guid timelineId, int count, string timelineName)
    {
        var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        var startTimes = new[] {
            new TimeSpan(8, 30, 0), new TimeSpan(10, 15, 0), new TimeSpan(12, 0, 0),
            new TimeSpan(13, 45, 0), new TimeSpan(15, 30, 0), new TimeSpan(17, 15, 0),
            new TimeSpan(19, 0, 0), new TimeSpan(20, 0, 0), new TimeSpan(21, 0, 0), new TimeSpan(22, 0, 0)
        };
        var names = new[] { "Мат. анализ", "Физика", "Информатика", "Химия", "История", "Философия", "Английский", "Физкультура", "Дискретная математика", "Линейная алгебра" };

        for (int i = 0; i < count; i++)
        {
            var duration = i % 3 == 0 ? TimeSpan.FromMinutes(90) : (i % 3 == 1 ? TimeSpan.FromMinutes(80) : TimeSpan.FromMinutes(100));
            var lesson = new Lesson
            {
                TimelineId = timelineId,
                Name = names[i],
                Description = $"[{timelineName}] Ауд. {100 + i * 10}",
                StartTime = startTimes[i],
                EndTime = startTimes[i].Add(duration),
                Type = (LessonType)(i % 4),
                Day = days[i % days.Length]
            };
            await repo.AddAsync(lesson);
        }
    }
}