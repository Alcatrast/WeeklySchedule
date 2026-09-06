using WeeklySchedule.Models;

namespace WeeklySchedule.Core;

public static class TimelineLayoutBuilder
{
    // Потолок числа колонок: НОК конкурентностей растет очень быстро, а сетка
    // из сотен колонок все равно нечитаема
    private const int MaxColumns = 24;

    private static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);

    public static TimelineLayout Build(DateTime date, List<Lesson> allLessons, DateTime now)
    {
        var dayLessons = allLessons.Where(l => l.Day == date.DayOfWeek).ToList();
        if (dayLessons.Count == 0)
            return new TimelineLayout { TotalMinutes = 0, TotalColumns = 1 };

        var minStart = dayLessons.Min(l => l.StartTime);
        var maxEnd = dayLessons.Max(l => l.EndTime);
        int totalMinutes = (int)(maxEnd - minStart).TotalMinutes;

        var timePoints = new SortedSet<TimeSpan>();
        foreach (var l in dayLessons)
        {
            timePoints.Add(l.StartTime);
            timePoints.Add(l.EndTime);
        }

        var segments = new List<TimeSegment>();
        var points = timePoints.ToList();
        for (int i = 0; i < points.Count - 1; i++)
        {
            segments.Add(new TimeSegment { Start = points[i], End = points[i + 1] });
        }
        var islands = new List<List<Lesson>>();
        var assigned = new HashSet<Lesson>();

        foreach (var lesson in dayLessons)
        {
            if (assigned.Contains(lesson)) continue;

            var island = new List<Lesson>();
            var queue = new Queue<Lesson>();
            queue.Enqueue(lesson);
            assigned.Add(lesson);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                island.Add(current);

                foreach (var other in dayLessons)
                {
                    if (!assigned.Contains(other))
                    {
                        if (current.StartTime < other.EndTime && current.EndTime > other.StartTime)
                        {
                            assigned.Add(other);
                            queue.Enqueue(other);
                        }
                    }
                }
            }
            islands.Add(island);
        }

        var allConcurrencies = new List<int>();
        var islandMaxC = new Dictionary<List<Lesson>, int>();

        foreach (var island in islands)
        {
            int maxC = 0;
            var events = new List<(TimeSpan Time, int Type)>();
            foreach (var l in island)
            {
                events.Add((l.StartTime, 1));
                events.Add((l.EndTime, -1));
            }
            events = [.. events.OrderBy(e => e.Time).ThenBy(e => e.Type)];

            int currentC = 0;
            foreach (var ev in events)
            {
                currentC += ev.Type;
                if (currentC > maxC) maxC = currentC;
            }

            int c = Math.Max(1, maxC);
            islandMaxC[island] = c;
            allConcurrencies.Add(c);
        }

        // Берем НОК конкурентностей: при нем каждый остров делит ширину нацело.
        // Накапливаем в long и обрываемся на потолке — в int произведение
        // нескольких взаимно простых конкурентностей переполняется
        long columns = 1;
        foreach (var c in allConcurrencies)
        {
            columns = columns / Gcd(columns, c) * c;
            if (columns >= MaxColumns)
            {
                columns = MaxColumns;
                break;
            }
        }

        // Колонок не может быть меньше, чем одновременных пар в самом плотном острове
        int maxConcurrency = allConcurrencies.Count > 0 ? allConcurrencies.Max() : 1;
        int totalColumns = (int)Math.Max(columns, maxConcurrency);

        var placements = new List<LessonPlacement>();

        foreach (var island in islands)
        {
            int c = islandMaxC[island];

            var sortedIsland = island.OrderBy(l => l.StartTime)
                                     .ThenByDescending(l => l.EndTime - l.StartTime)
                                     .ThenBy(l => l.Name)
                                     .ToList();

            var colEndTimes = new TimeSpan[c];
            for (int i = 0; i < c; i++) colEndTimes[i] = TimeSpan.MinValue;

            foreach (var lesson in sortedIsland)
            {
                int colIndex = 0;
                for (int i = 0; i < c; i++)
                {
                    if (colEndTimes[i] <= lesson.StartTime)
                    {
                        colIndex = i;
                        break;
                    }
                }
                colEndTimes[colIndex] = lesson.EndTime;

                int startRow = segments.FindIndex(s => s.Start == lesson.StartTime);
                int endRow = segments.FindIndex(s => s.End == lesson.EndTime);
                int rowSpan = Math.Max(1, endRow - startRow + 1);
                int lessonMinutes = (int)(lesson.EndTime - lesson.StartTime).TotalMinutes;
                bool isCurrent = now.TimeOfDay >= lesson.StartTime && now.TimeOfDay < lesson.EndTime && now.Date == date;

                // Границы колонки считаем от краев сетки, а не как colIndex * span:
                // если НОК уперся в потолок, totalColumns может не делиться на c,
                // и колонки одинаковой ширины оставили бы пустую полосу справа
                int colStart = colIndex * totalColumns / c;
                int colEnd = (colIndex + 1) * totalColumns / c;

                placements.Add(new LessonPlacement
                {
                    Lesson = lesson,
                    StartRow = startRow,
                    RowSpan = rowSpan,
                    TotalMinutes = lessonMinutes,
                    Column = colStart,
                    ColumnSpan = Math.Max(1, colEnd - colStart),
                    IsCurrent = isCurrent
                });
            }
        }

        var layout = new TimelineLayout
        {
            TotalMinutes = totalMinutes,
            TotalColumns = totalColumns,
            Lessons = placements,
            Segments = segments
        };
        RefreshState(layout, date, now);
        return layout;
    }

    // Геометрия не зависит от текущего времени. Меняем только состояние карточек
    // и маркер перерыва, сохраняя объекты размещения.
    public static void RefreshState(TimelineLayout layout, DateTime date, DateTime now)
    {
        foreach (var placement in layout.Lessons)
            placement.IsCurrent = date.Date == now.Date && now.TimeOfDay >= placement.Lesson.StartTime &&
                now.TimeOfDay < placement.Lesson.EndTime;
        var segments = layout.Segments;
        var dayLessons = layout.Lessons.Select(p => p.Lesson).ToList();
        // Рисуется ровно один разделитель — маркер текущего времени в перерыве.
        // Прошедшие перерывы в список не попадают, поэтому и флага "перерыв
        // в прошлом" здесь больше нет: он не мог стать true ни у одного элемента
        var breaks = new List<BreakPlacement>();
        if (date.Date == now.Date)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];

                bool isBreak = !dayLessons.Any(l => l.StartTime < seg.End && l.EndTime > seg.Start);
                if (!isBreak) continue;

                bool isCurrentBreak = now.TimeOfDay >= seg.Start && now.TimeOfDay <= seg.End;
                if (!isCurrentBreak) continue;

                // Перерыв может состоять из нескольких подряд идущих сегментов
                var last = breaks.Count > 0 ? breaks[^1] : null;
                if (last != null && last.StartRow + last.RowSpan == i)
                {
                    last.RowSpan++;
                    last.TotalMinutes += seg.DurationMinutes;
                }
                else
                {
                    breaks.Add(new BreakPlacement
                    {
                        StartRow = i,
                        RowSpan = 1,
                        TotalMinutes = seg.DurationMinutes,
                        Type = SeparatorType.ThickWhite
                    });
                }
            }
        }

        layout.Breaks = breaks;
    }
}
