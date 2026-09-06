using WeeklySchedule.Core;
using WeeklySchedule.Models;

static class TimelineMetricsRegression
{
    public static (string, Func<Task>)[] Tests =>
    [
        ("Same lesson has the same height on every day", SameHeightAcrossDays),
        ("Card height equals the sum of its rows", SpanMatchesRows),
        ("Long gap is capped and labelled", GapIsCapped),
        ("Short lesson keeps the minimum height", ShortLessonFloor),
        ("Empty day yields no rows", EmptyDay)
    ];

    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }

    private static readonly DateTime Monday = new(2026, 9, 7);
    private static readonly DateTime Tuesday = new(2026, 9, 8);

    private static Lesson Pair(DayOfWeek day, string start, string end, string name = "Пара") => new()
    {
        Day = day, Name = name,
        StartTime = TimeSpan.Parse(start), EndTime = TimeSpan.Parse(end)
    };

    private static double HeightOf(TimelineLayout layout, double[] rows, string name)
    {
        var placement = layout.Lessons.Single(p => p.Lesson.Name == name);
        return TimelineMetrics.SpanHeight(rows, placement.StartRow, placement.RowSpan);
    }

    // Исходный баг: масштаб считался от самой короткой пары дня, поэтому день с одной
    // короткой парой растягивал все остальные свои пары относительно других дней.
    private static Task SameHeightAcrossDays()
    {
        var plain = new List<Lesson>
        {
            Pair(DayOfWeek.Monday, "09:00", "10:25", "Общая"),
            Pair(DayOfWeek.Monday, "10:35", "12:00", "Вторая")
        };
        var withShort = new List<Lesson>
        {
            Pair(DayOfWeek.Tuesday, "09:00", "10:25", "Общая"),
            Pair(DayOfWeek.Tuesday, "10:35", "12:00", "Вторая"),
            Pair(DayOfWeek.Tuesday, "12:10", "12:50", "Короткая")
        };
        var now = Monday.AddHours(3);
        var first = TimelineLayoutBuilder.Build(Monday, plain, now);
        var second = TimelineLayoutBuilder.Build(Tuesday, withShort, now);
        var firstRows = TimelineMetrics.RowHeights(first);
        var secondRows = TimelineMetrics.RowHeights(second);

        Check(HeightOf(first, firstRows, "Общая") == HeightOf(second, secondRows, "Общая"));
        Check(HeightOf(first, firstRows, "Вторая") == HeightOf(second, secondRows, "Вторая"));
        Check(HeightOf(first, firstRows, "Общая") == TimelineMetrics.LessonHeight(85));
        return Task.CompletedTask;
    }

    // Пара, растянутая на несколько сегментов, должна занимать ровно сумму своих строк:
    // раньше высота карточки считалась отдельно от высот строк и с ними расходилась.
    private static Task SpanMatchesRows()
    {
        var lessons = new List<Lesson>
        {
            Pair(DayOfWeek.Monday, "09:00", "12:00", "Длинная"),
            Pair(DayOfWeek.Monday, "10:00", "11:00", "Параллельная"),
            Pair(DayOfWeek.Monday, "12:10", "13:35", "Обычная")
        };
        var layout = TimelineLayoutBuilder.Build(Monday, lessons, Monday.AddHours(3));
        var rows = TimelineMetrics.RowHeights(layout);
        Check(rows.Length == layout.Segments.Count);
        foreach (var placement in layout.Lessons)
        {
            double span = TimelineMetrics.SpanHeight(rows, placement.StartRow, placement.RowSpan);
            Check(Math.Abs(span - TimelineMetrics.LessonHeight(placement.TotalMinutes)) < 0.001);
        }
        Check(TimelineMetrics.TopOffset(rows, 0) == 0);
        Check(TimelineMetrics.TopOffset(rows, 1) == rows[0]);
        return Task.CompletedTask;
    }

    private static Task GapIsCapped()
    {
        var lessons = new List<Lesson>
        {
            Pair(DayOfWeek.Tuesday, "12:10", "13:35", "До окна"),
            Pair(DayOfWeek.Tuesday, "15:30", "16:55", "После окна")
        };
        var layout = TimelineLayoutBuilder.Build(Tuesday, lessons, Tuesday.AddHours(9));
        var rows = TimelineMetrics.RowHeights(layout);
        int gapRow = Enumerable.Range(0, layout.Segments.Count).Single(i => TimelineMetrics.IsGapRow(layout, i));

        Check(layout.Segments[gapRow].DurationMinutes == 115);
        Check(rows[gapRow] == TimelineMetrics.MaxGapHeight);
        Check(TimelineMetrics.FormatGap(115) == "окно 1 ч 55 мин");
        Check(TimelineMetrics.FormatGap(120) == "окно 2 ч");
        Check(TimelineMetrics.FormatGap(10) == "перерыв 10 мин");
        return Task.CompletedTask;
    }

    private static Task ShortLessonFloor()
    {
        var lessons = new List<Lesson> { Pair(DayOfWeek.Monday, "09:00", "09:15", "Короткая") };
        var layout = TimelineLayoutBuilder.Build(Monday, lessons, Monday.AddHours(3));
        var rows = TimelineMetrics.RowHeights(layout);
        Check(HeightOf(layout, rows, "Короткая") == TimelineMetrics.MinLessonHeight);
        // Шестичасовая пара упирается в потолок, а не растет бесконечно.
        Check(TimelineMetrics.LessonHeight(360) == TimelineMetrics.MaxLessonHeight);
        Check(TimelineMetrics.LessonHeight(240) < TimelineMetrics.MaxLessonHeight);
        return Task.CompletedTask;
    }

    private static Task EmptyDay()
    {
        var layout = TimelineLayoutBuilder.Build(Monday, [], Monday.AddHours(3));
        var rows = TimelineMetrics.RowHeights(layout);
        Check(rows.Length == 0);
        Check(TimelineMetrics.SpanHeight(rows, 0, 1) == 0);
        Check(TimelineMetrics.TopOffset(rows, 3) == 0);
        return Task.CompletedTask;
    }
}
