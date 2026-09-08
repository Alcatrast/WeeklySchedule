using WeeklySchedule.Models;

namespace WeeklySchedule.Core;

/// <summary>
/// Общая сетка недели: строки одни и те же для всех семи дней, поэтому одно и то же
/// время лежит на одной высоте в любом дне, а свободный день занимает столько же
/// места, сколько заполненный. Раньше строки строились из точек времени только
/// своего дня, и при перелистывании разметка не совпадала.
///
/// Здесь не должно появляться типов MAUI: Core целиком уезжает в тестовый проект.
/// </summary>
public sealed class WeekLayout
{
    private static readonly DayOfWeek[] AllDays =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    private readonly Dictionary<DayOfWeek, TimelineLayout> _days;

    public List<TimeSegment> Segments { get; }
    public double[] RowHeights { get; }
    public double TotalHeight { get; }

    private WeekLayout(List<TimeSegment> segments, double[] rowHeights,
        Dictionary<DayOfWeek, TimelineLayout> days)
    {
        Segments = segments;
        RowHeights = rowHeights;
        TotalHeight = TimelineMetrics.TotalHeight(rowHeights);
        _days = days;
    }

    public TimelineLayout For(DayOfWeek day) => _days[day];

    public static WeekLayout Build(List<Lesson> lessons, IReadOnlyList<BaseDay> baseDays)
    {
        // Точки времени собираем со всей недели, а не с одного дня: каждая граница
        // любого дня обязана быть строкой в сетке каждого дня, иначе выравнивание
        // «строчка в строчку» не получится
        var timePoints = new SortedSet<TimeSpan>();
        foreach (var lesson in lessons)
        {
            // Пара с концом не позже начала не дает строки сетки, и показать ее негде.
            // Это инвариант, а не фильтр данных: такие пары не должны доезжать сюда —
            // разбор их отбрасывает, а осевшие в хранилище уходят вместе со всем
            // прежним содержимым на любом из двух путей импорта
            if (lesson.EndTime <= lesson.StartTime) continue;
            timePoints.Add(lesson.StartTime);
            timePoints.Add(lesson.EndTime);
        }
        foreach (var marker in baseDays)
        {
            if (marker.AllDay || marker.EndTime <= marker.StartTime) continue;
            timePoints.Add(marker.StartTime);
            timePoints.Add(marker.EndTime);
        }

        var points = timePoints.ToList();
        var segments = new List<TimeSegment>(Math.Max(0, points.Count - 1));
        for (int i = 0; i < points.Count - 1; i++)
            segments.Add(new TimeSegment { Start = points[i], End = points[i + 1] });

        // Карты вместо перебора сегментов на каждую пару: поиск строки был
        // O(сегменты) и повторялся для каждой пары каждого дня
        var rowByStart = new Dictionary<TimeSpan, int>(segments.Count);
        var rowByEnd = new Dictionary<TimeSpan, int>(segments.Count);
        for (int i = 0; i < segments.Count; i++)
        {
            rowByStart[segments[i].Start] = i;
            rowByEnd[segments[i].End] = i;
        }

        var byDay = lessons.Where(l => l.EndTime > l.StartTime)
            .GroupBy(l => l.Day)
            .ToDictionary(g => g.Key, g => g.ToList());

        var days = new Dictionary<DayOfWeek, TimelineLayout>(AllDays.Length);
        var weekPlacements = new List<LessonPlacement>();
        foreach (var day in AllDays)
        {
            var dayLessons = byDay.TryGetValue(day, out var found) ? found : [];
            var layout = TimelineLayoutBuilder.BuildDay(dayLessons, rowByStart, rowByEnd);
            layout.Segments = segments;
            days[day] = layout;
            weekPlacements.AddRange(layout.Lessons);
        }

        // Высоты — один раз по размещениям всей недели. Считать их по дню значило бы
        // раздувать строки под пары именно этого дня, и высоты разошлись бы
        var gapRows = TimelineMetrics.GapRows(segments, weekPlacements);
        var rowHeights = TimelineMetrics.RowHeights(segments, weekPlacements, gapRows);

        foreach (var day in AllDays)
        {
            var layout = days[day];
            layout.RowHeights = rowHeights;
            layout.GapRows = gapRows;
            layout.Markers = BuildMarkers(baseDays, day, segments);
        }

        return new WeekLayout(segments, rowHeights, days);
    }

    private static List<MarkerPlacement> BuildMarkers(IReadOnlyList<BaseDay> baseDays,
        DayOfWeek day, List<TimeSegment> segments)
    {
        var markers = new List<MarkerPlacement>();
        foreach (var marker in baseDays)
        {
            if (marker.Day != day) continue;

            int startRow, rowSpan;
            if (marker.AllDay || marker.EndTime <= marker.StartTime)
            {
                // Пометка на весь день покрывает сетку целиком. Единица снизу — на
                // случай недели вообще без пар: сетки нет, но показать пометку надо
                startRow = 0;
                rowSpan = Math.Max(1, segments.Count);
            }
            else
            {
                int first = -1, last = -1;
                for (int i = 0; i < segments.Count; i++)
                {
                    if (segments[i].End <= marker.StartTime) continue;
                    if (segments[i].Start >= marker.EndTime) break;
                    if (first < 0) first = i;
                    last = i;
                }
                if (first < 0) continue;
                startRow = first;
                rowSpan = last - first + 1;
            }

            markers.Add(new MarkerPlacement
            {
                Marker = marker,
                StartRow = startRow,
                RowSpan = rowSpan,
                Text = marker.DisplayText
            });
        }
        markers.Sort((a, b) => a.StartRow.CompareTo(b.StartRow));
        return markers;
    }
}
