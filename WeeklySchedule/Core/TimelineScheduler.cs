using WeeklySchedule.Models;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Core;

public class TimelineScheduler
{
    private readonly Lock _lock = new();
    private CancellationTokenSource? _cts;
    private List<DateTime> _markers = [];
    private DateTime _currentDate;
    private List<Lesson> _allLessons = [];

    public event Action<DateTime>? OnTimeMarkerReached;
    public event Action? OnDayChanged;

    public void Initialize(List<Lesson> allLessons, DateTime currentDate)
    {
        _allLessons = allLessons;
        _currentDate = currentDate.Date;
        BuildMarkersAndStart();
    }

    public void RebuildQueue()
    {
        var newDate = TimeContext.Now.Date;
        if (newDate != _currentDate)
        {
            _currentDate = newDate;
            MainThread.BeginInvokeOnMainThread(() => OnDayChanged?.Invoke());
        }
        BuildMarkersAndStart();
    }

    private void BuildMarkersAndStart()
    {
        lock (_lock)
        {
            BuildMarkers();
            RestartTimer();
        }
    }

    private void BuildMarkers()
    {
        _markers.Clear();
        _markers.Add(_currentDate.AddDays(1).Date);
        var dayOfWeek = _currentDate.DayOfWeek;
        var relevantLessons = _allLessons.Where(l => l.Day == dayOfWeek);

        foreach (var lesson in relevantLessons)
        {
            _markers.Add(_currentDate.Add(lesson.StartTime));
            _markers.Add(_currentDate.Add(lesson.EndTime));
        }
        _markers = [.. _markers.Distinct().OrderBy(m => m)];
        var now = TimeContext.Now;
        _markers = [.. _markers.Where(m => m > now)];
    }

    private void RestartTimer()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    DateTime nextMarker;
                    lock (_lock)
                    {
                        nextMarker = _markers.Count > 0 ? _markers[0] : _currentDate.AddDays(1).Date;
                    }

                    var now = TimeContext.Now;
                    TimeSpan delay = nextMarker - now;

                    if (delay < TimeSpan.FromMilliseconds(50))
                        delay = TimeSpan.FromMilliseconds(50);

                    await Task.Delay(delay, token);

                    if (token.IsCancellationRequested) break;

                    now = TimeContext.Now;
                    bool isDayChange = false;

                    lock (_lock)
                    {
                        if (now.Date > _currentDate)
                        {
                            isDayChange = true;
                            // Без обновления даты следующий маркер снова оказался бы в прошлом,
                            // и цикл крутился бы с шагом 50 мс до тех пор, пока подписчик
                            // не вызовет RebuildQueue
                            _currentDate = now.Date;
                        }
                        _markers.RemoveAll(m => m <= now);
                    }

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (isDayChange) OnDayChanged?.Invoke();
                        else OnTimeMarkerReached?.Invoke(now);
                    });
                }
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    public void Stop()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _markers.Clear();
        }
    }
}