namespace WeeklySchedule.Models;

public class TimeSegment
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public int DurationMinutes => (int)(End - Start).TotalMinutes;
}

public class LessonPlacement
{
    public Lesson Lesson { get; set; } = null!;
    public int StartRow { get; set; }
    public int RowSpan { get; set; }
    public int TotalMinutes { get; set; }
    public int Column { get; set; }
    public int ColumnSpan { get; set; }
    public bool IsCurrent { get; set; }
}

public class BreakPlacement
{
    public int StartRow { get; set; }
    public int RowSpan { get; set; }
    public int TotalMinutes { get; set; }
    public SeparatorType Type { get; set; } = SeparatorType.None;
}
public class TimelineLayout
{
    public int TotalMinutes { get; set; }
    public int TotalColumns { get; set; }
    public List<LessonPlacement> Lessons { get; set; } = [];
    public List<BreakPlacement> Breaks { get; set; } = [];
    public List<TimeSegment> Segments { get; set; } = [];
}