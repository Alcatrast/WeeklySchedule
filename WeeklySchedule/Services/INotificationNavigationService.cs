namespace WeeklySchedule.Services;

public interface INotificationNavigationService
{
    Guid? PendingTimelineId { get; }
    event Action? NavigationRequested;
    void SetPendingNavigation(Guid timelineId);
    void ClearPendingNavigation();
}

public class NotificationNavigationService : INotificationNavigationService
{
    public Guid? PendingTimelineId { get; private set; }
    public event Action? NavigationRequested;

    public void SetPendingNavigation(Guid timelineId)
    {
        PendingTimelineId = timelineId;
        NavigationRequested?.Invoke();
    }

    public void ClearPendingNavigation()
    {
        PendingTimelineId = null;
    }
}
