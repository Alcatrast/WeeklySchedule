using WeeklySchedule.Models;

namespace WeeklySchedule.Core;

public static class TimelineLayoutBuilder
{
    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
    private static int Lcm(int a, int b) => (a / Gcd(a, b)) * b;

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

        int totalColumns = 1;
        foreach (var c in allConcurrencies)
        {
            totalColumns = Lcm(totalColumns, c);
        }
        totalColumns = Math.Min(totalColumns, 24);

        var placements = new List<LessonPlacement>();

        foreach (var island in islands)
        {
            int c = islandMaxC[island];
            int span = totalColumns / c;

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

                placements.Add(new LessonPlacement
                {
                    Lesson = lesson,
                    StartRow = startRow,
                    RowSpan = rowSpan,
                    TotalMinutes = lessonMinutes,
                    Column = colIndex * span,
                    ColumnSpan = span,
                    IsCurrent = isCurrent
                });
            }
        }

        var breaks = new List<BreakPlacement>();
        bool isToday = date.Date == now.Date;
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            bool isBreak = !dayLessons.Any(l => l.StartTime < seg.End && l.EndTime > seg.Start);
            if (isBreak)
            {
                var breakEnd = seg.Start + TimeSpan.FromMinutes(seg.DurationMinutes);
                bool isPast = isToday && breakEnd <= now.TimeOfDay;

                bool isCurrentBreak = isToday && now.TimeOfDay >= seg.Start && now.TimeOfDay <= breakEnd;

                SeparatorType type = SeparatorType.None;
                if (isCurrentBreak)
                {
                    type = SeparatorType.ThickWhite;
                }
                else
                {
                    type = SeparatorType.None;
                }

                if (breaks.Count != 0 && breaks.Last().StartRow + breaks.Last().RowSpan == i && breaks.Last().Type == type && type != SeparatorType.None)
                {
                    breaks.Last().RowSpan++;
                    breaks.Last().TotalMinutes += seg.DurationMinutes;
                }
                else if (type != SeparatorType.None)
                {
                    breaks.Add(new BreakPlacement
                    {
                        StartRow = i,
                        RowSpan = 1,
                        TotalMinutes = seg.DurationMinutes,
                        Type = type,
                        IsPast = isPast
                    });
                }
            }
        }

        return new TimelineLayout
        {
            TotalMinutes = totalMinutes,
            TotalColumns = totalColumns,
            Lessons = placements,
            Breaks = breaks,
            Segments = segments,
            DayStartTime = date.Date.Add(minStart)
        };
    }
}