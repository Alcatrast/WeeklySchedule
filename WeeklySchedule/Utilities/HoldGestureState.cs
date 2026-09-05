namespace WeeklySchedule.Utilities;

// Чистая логика удержания: отменённый свайп не становится удержанием, а отпускание
// после удержания не становится коротким нажатием. Пороги задаёт платформа.
public sealed class HoldGestureState
{
    private int _version;
    private double _x, _y;
    private bool _tracking;
    public bool Held { get; private set; }
    public bool Cancelled { get; private set; }

    public int Begin(double x, double y)
    {
        _x = x; _y = y;
        _tracking = true;
        Held = Cancelled = false;
        return ++_version;
    }

    public void Move(double x, double y, double slop)
    {
        if (Math.Abs(x - _x) > slop || Math.Abs(y - _y) > slop) Cancel();
    }

    public bool TryHold(int version)
    {
        if (!_tracking || version != _version) return false;
        _tracking = false;
        Held = true;
        ++_version;
        return true;
    }

    public bool End()
    {
        _tracking = false;
        ++_version;
        return Held;
    }

    public void Cancel()
    {
        _tracking = false;
        Cancelled = true;
        ++_version;
    }
}
