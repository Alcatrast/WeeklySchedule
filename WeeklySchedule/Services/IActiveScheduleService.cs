namespace WeeklySchedule.Services;

public interface IActiveScheduleService
{
    Guid ActiveTimelineId { get; set; }
    event Action<Guid> ActiveTimelineChanged;
}