using WeeklySchedule.Models;

namespace WeeklySchedule.Core;

public static class TimelineLayoutBuilder
{
    // Потолок числа колонок: НОК конкурентностей растет очень быстро, а сетка
    // из сотен колонок все равно нечитаема
    private const int MaxColumns = 24;

    private static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);

    /// <summary>
    /// Размещения одного дня в общей сетке недели. Строки не считаются от пар этого
    /// дня: их индексы берутся из карт общей сетки, поэтому одно и то же время лежит
    /// на одной строке во всех днях.
    /// </summary>
    public static TimelineLayout BuildDay(
        List<Lesson> dayLessons,
        IReadOnlyDictionary<TimeSpan, int> rowByStart,
        IReadOnlyDictionary<TimeSpan, int> rowByEnd)
    {
        if (dayLessons.Count == 0) return new TimelineLayout { TotalColumns = 1 };

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

                // Границы пары всегда входят в точки общей сетки, поэтому промах
                // невозможен; запасной ноль оставлен на случай испорченных данных
                int startRow = rowByStart.TryGetValue(lesson.StartTime, out var s) ? s : 0;
                int endRow = rowByEnd.TryGetValue(lesson.EndTime, out var e) ? e : startRow;
                int rowSpan = Math.Max(1, endRow - startRow + 1);
                int lessonMinutes = (int)(lesson.EndTime - lesson.StartTime).TotalMinutes;

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
                    ColumnSpan = Math.Max(1, colEnd - colStart)
                });
            }
        }

        return new TimelineLayout { TotalColumns = totalColumns, Lessons = placements };
    }

    /// <summary>
    /// Геометрия не зависит от текущего времени. Меняем только подсветку текущей пары
    /// и положение метки времени, сохраняя объекты размещения.
    ///
    /// Возвращает, изменилось ли хоть что-нибудь. Планировщик будит экран раз в минуту,
    /// а перерисовывать нечего почти всегда: в чужом дне, ночью и в выходной ни одна
    /// пара не текущая и метки времени нет вовсе.
    /// </summary>
    public static bool RefreshState(TimelineLayout layout, DateTime date, DateTime now)
    {
        bool today = date.Date == now.Date;
        bool changed = false;
        foreach (var placement in layout.Lessons)
        {
            bool current = today && now.TimeOfDay >= placement.Lesson.StartTime &&
                now.TimeOfDay < placement.Lesson.EndTime;
            if (placement.IsCurrent == current) continue;
            placement.IsCurrent = current;
            changed = true;
        }

        var segments = layout.Segments;
        // Метка живет только внутри сетки: до первой пары недели и после последней
        // ее прижимало бы к краю, и она врала бы о том, где сейчас время
        bool inside = today && segments.Count > 0 &&
            now.TimeOfDay >= segments[0].Start && now.TimeOfDay <= segments[^1].End;
        double? offset = inside
            ? TimelineMetrics.OffsetAt(layout.RowHeights, segments, now.TimeOfDay)
            : null;
        if (offset != layout.CurrentTimeOffset)
        {
            layout.CurrentTimeOffset = offset;
            changed = true;
        }
        return changed;
    }
}
