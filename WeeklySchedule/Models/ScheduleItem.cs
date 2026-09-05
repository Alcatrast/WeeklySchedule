namespace WeeklySchedule.Models;

public enum SeparatorType
{
    None,       // Невидимый (высота 0)
    ThickWhite  // Толстый белый — маркер текущего времени в перерыве
}


public abstract class ScheduleItem { }

public class LessonItem : ScheduleItem
{
    public Lesson Lesson { get; set; } = null!;
    public bool IsPast { get; set; }
    public bool IsCurrent { get; set; }
}

public class SeparatorItem : ScheduleItem
{
    public SeparatorType Type { get; set; }
    public bool IsPast { get; set; }
}