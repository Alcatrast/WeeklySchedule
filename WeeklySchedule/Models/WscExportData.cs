namespace WeeklySchedule.Models;

public class WscExportData
{
    public string TimelineName { get; set; } = string.Empty;
    public List<Lesson> Lessons { get; set; } = new();
}