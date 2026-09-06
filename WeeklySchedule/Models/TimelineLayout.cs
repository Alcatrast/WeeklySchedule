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

// Пометка расписания в сетке: базовый день. Занимает строки, но не колонки —
// рисуется во всю ширину под карточками пар.
public class MarkerPlacement
{
    public BaseDay Marker { get; set; } = null!;
    public int StartRow { get; set; }
    public int RowSpan { get; set; }
    public string Text { get; set; } = string.Empty;
}

// Раскладка одного дня. Segments и RowHeights общие для всей недели: их держит
// WeekLayout и раздает всем дням один и тот же экземпляр, иначе одно и то же
// время оказывалось бы на разной высоте в разных днях.
public class TimelineLayout
{
    public int TotalColumns { get; set; }
    public List<LessonPlacement> Lessons { get; set; } = [];
    public List<MarkerPlacement> Markers { get; set; } = [];
    public List<TimeSegment> Segments { get; set; } = [];
    public double[] RowHeights { get; set; } = [];

    // Строки, свободные во всех днях недели — их и подписываем «окно».
    public bool[] GapRows { get; set; } = [];

    // Смещение метки текущего времени от верха сетки; null — день не сегодняшний.
    public double? CurrentTimeOffset { get; set; }
}
