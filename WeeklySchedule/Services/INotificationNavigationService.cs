namespace WeeklySchedule.Services;

public interface INotificationNavigationService
{
    Guid? PendingTimelineId { get; }
    void SetPendingNavigation(Guid timelineId);
    void ClearPendingNavigation();
}

public class NotificationNavigationService : INotificationNavigationService
{
    public Guid? PendingTimelineId { get; private set; }

    public void SetPendingNavigation(Guid timelineId)
    {
        PendingTimelineId = timelineId;
    }

    public void ClearPendingNavigation()
    {
        PendingTimelineId = null;
    }
}