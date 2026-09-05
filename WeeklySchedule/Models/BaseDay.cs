namespace WeeklySchedule.Models;

// Пометка расписания, не занятие: не участвует в сетке пар и уведомлениях.
public sealed record BaseDay
{
    public DayOfWeek Day { get; init; }
    public bool AllDay { get; init; }
    public TimeSpan StartTime { get; init; }
    public TimeSpan EndTime { get; init; }
    public string Text { get; init; } = "Базовый день";

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayText => AllDay ? Text : $"{Text} · {StartTime:hh\\:mm}–{EndTime:hh\\:mm}";
}
