namespace WeeklySchedule.ViewModels;

// Одна подписка на текущий день; при переиспользовании View старый день отпускается.
public sealed class DayViewSubscription(Action onLayout, Action onScroll) : IDisposable
{
    private DayViewModel? _source;

    public void SetSource(DayViewModel? source)
    {
        if (ReferenceEquals(_source, source)) return;
        if (_source != null)
        {
            _source.LayoutUpdated -= onLayout;
            _source.ScrollToCurrentRequested -= onScroll;
        }
        _source = source;
        if (_source != null)
        {
            _source.LayoutUpdated += onLayout;
            _source.ScrollToCurrentRequested += onScroll;
        }
    }

    public void Dispose() => SetSource(null);
}
