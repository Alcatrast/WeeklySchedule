namespace WeeklySchedule.Services;

public class ActiveScheduleService : IActiveScheduleService
{
    private const string PreferenceKey = "ActiveTimelineId";
    private Guid _activeId;

    public event Action<Guid>? ActiveTimelineChanged;

    public Guid ActiveTimelineId
    {
        get
        {
            if (_activeId == Guid.Empty)
            {
                var saved = Preferences.Get(PreferenceKey, string.Empty);
                if (Guid.TryParse(saved, out var id))
                {
                    _activeId = id;
                }
            }
            return _activeId;
        }
        set
        {
            if (_activeId != value)
            {
                _activeId = value;
                Preferences.Set(PreferenceKey, value.ToString());
                ActiveTimelineChanged?.Invoke(value);
            }
        }
    }
}