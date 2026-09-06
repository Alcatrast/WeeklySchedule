namespace WeeklySchedule.Models;

public class Lesson
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TimelineId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public LessonType Type { get; set; }
    public DayOfWeek Day { get; set; }

    /// <summary>
    /// Пара пришла из разбора xlsx. Повторный разбор сносит только такие пары:
    /// заведенные руками переживают обновление расписания.
    /// </summary>
    public bool FromImport { get; set; }
}