namespace WeeklySchedule.Models;

public class Timeline
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool NotificationsEnabled { get; set; } = true; // Новое свойство
}