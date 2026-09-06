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
        ("Empty week yields no rows", EmptyWeek),
        ("One time sits at one offset in every day", SharedGrid),
        ("Free day is as tall as a full one", FreeDayKeepsHeight),
        ("Base day is a block, and a timed one adds grid lines", BaseDayBlocks),
        ("Current-time marker interpolates inside a segment", CurrentTimeMarker)
    ];

    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }

    private static readonly DateTime Monday = new(2026, 9, 7);
    private static readonly DateTime Tuesday = new(2026, 9, 8);

    private static Lesson Pair(DayOfWeek day, string start, string end, string name = "Пара") => new()
    {
        Day = day, Name = name,
        StartTime = TimeSpan.Parse(start), EndTime = TimeSpan.Parse(end)
    };

    private static double HeightOf(TimelineLayout layout, string name)
    {
        var placement = layout.Lessons.Single(p => p.Lesson.Name == name);
        return TimelineMetrics.SpanHeight(layout.RowHeights, placement.StartRow, placement.RowSpan);
    }

    private static double TopOf(TimelineLayout layout, string name)
    {
        var placement = layout.Lessons.Single(p => p.Lesson.Name == name);
        return TimelineMetrics.TopOffset(layout.RowHeights, placement.StartRow);
    }

    // Исходный баг: масштаб считался от самой короткой пары дня, поэтому день с одной
    // короткой парой растягивал все остальные свои пары относительно других дней.
    private static Task SameHeightAcrossDays()
    {
        var week = WeekLayout.Build(
        [
            Pair(DayOfWeek.Monday, "09:00", "10:25", "ПН первая"),
            Pair(DayOfWeek.Monday, "10:35", "12:00", "ПН вторая"),
            Pair(DayOfWeek.Tuesday, "09:00", "10:25", "ВТ первая"),
            Pair(DayOfWeek.Tuesday, "10:35", "12:00", "ВТ вторая"),
            Pair(DayOfWeek.Tuesday, "12:10", "12:50", "ВТ короткая")
        ], []);

        var monday = week.For(DayOfWeek.Monday);
        var tuesday = week.For(DayOfWeek.Tuesday);
        Check(HeightOf(monday, "ПН первая") == HeightOf(tuesday, "ВТ первая"));
        Check(HeightOf(monday, "ПН вторая") == HeightOf(tuesday, "ВТ вторая"));
        Check(HeightOf(monday, "ПН первая") == TimelineMetrics.LessonHeight(85));
        return Task.CompletedTask;
    }

    // Пара, растянутая на несколько сегментов, должна занимать ровно сумму своих строк:
    // раньше высота карточки считалась отдельно от высот строк и с ними расходилась.
    private static Task SpanMatchesRows()
    {
        var week = WeekLayout.Build(
        [
            Pair(DayOfWeek.Monday, "09:00", "12:00", "Длинная"),
            Pair(DayOfWeek.Monday, "10:00", "11:00", "Параллельная"),
            Pair(DayOfWeek.Monday, "12:10", "13:35", "Обычная")
        ], []);

        var layout = week.For(DayOfWeek.Monday);
        Check(layout.RowHeights.Length == layout.Segments.Count);
        foreach (var placement in layout.Lessons)
        {
            double span = TimelineMetrics.SpanHeight(layout.RowHeights, placement.StartRow, placement.RowSpan);
            Check(Math.Abs(span - TimelineMetrics.LessonHeight(placement.TotalMinutes)) < 0.001);
        }
        Check(TimelineMetrics.TopOffset(layout.RowHeights, 0) == 0);
        Check(TimelineMetrics.TopOffset(layout.RowHeights, 1) == layout.RowHeights[0]);
        return Task.CompletedTask;
    }

    private static Task GapIsCapped()
    {
        var week = WeekLayout.Build(
        [
            Pair(DayOfWeek.Tuesday, "12:10", "13:35", "До окна"),
            Pair(DayOfWeek.Tuesday, "15:30", "16:55", "После окна")
        ], []);

        var layout = week.For(DayOfWeek.Tuesday);
        int gapRow = Enumerable.Range(0, layout.Segments.Count).Single(i => layout.GapRows[i]);

        Check(layout.Segments[gapRow].DurationMinutes == 115);
        Check(layout.RowHeights[gapRow] == TimelineMetrics.MaxGapHeight);
        Check(TimelineMetrics.FormatGap(115) == "окно 1 ч 55 мин");
        Check(TimelineMetrics.FormatGap(120) == "окно 2 ч");
        Check(TimelineMetrics.FormatGap(10) == "перерыв 10 мин");
        return Task.CompletedTask;
    }

    private static Task ShortLessonFloor()
    {
        var week = WeekLayout.Build([Pair(DayOfWeek.Monday, "09:00", "09:15", "Короткая")], []);
        Check(HeightOf(week.For(DayOfWeek.Monday), "Короткая") == TimelineMetrics.MinLessonHeight);
        // Шестичасовая пара упирается в потолок, а не растет бесконечно.
        Check(TimelineMetrics.LessonHeight(360) == TimelineMetrics.MaxLessonHeight);
        Check(TimelineMetrics.LessonHeight(240) < TimelineMetrics.MaxLessonHeight);
        return Task.CompletedTask;
    }

    private static Task EmptyWeek()
    {
        var week = WeekLayout.Build([], []);
        Check(week.Segments.Count == 0 && week.RowHeights.Length == 0 && week.TotalHeight == 0);
        var layout = week.For(DayOfWeek.Monday);
        Check(layout.Lessons.Count == 0 && layout.Markers.Count == 0);
        Check(TimelineMetrics.SpanHeight(layout.RowHeights, 0, 1) == 0);
        Check(TimelineMetrics.TopOffset(layout.RowHeights, 3) == 0);
        Check(TimelineMetrics.OffsetAt(layout.RowHeights, layout.Segments, TimeSpan.FromHours(10)) == 0);
        return Task.CompletedTask;
    }

    // Ради этого сетка и стала общей: при перелистывании строки должны совпадать.
    private static Task SharedGrid()
    {
        var week = WeekLayout.Build(
        [
            Pair(DayOfWeek.Monday, "09:00", "10:25", "ПН"),
            Pair(DayOfWeek.Tuesday, "09:00", "10:25", "ВТ"),
            // Своя граница в среду обязана появиться в сетке всех дней
            Pair(DayOfWeek.Wednesday, "09:40", "11:05", "СР")
        ], []);

        var monday = week.For(DayOfWeek.Monday);
        var tuesday = week.For(DayOfWeek.Tuesday);
        // Строки и высоты — один и тот же экземпляр у всех дней
        Check(ReferenceEquals(monday.Segments, tuesday.Segments));
        Check(ReferenceEquals(monday.RowHeights, tuesday.RowHeights));
        Check(TopOf(monday, "ПН") == TopOf(tuesday, "ВТ"));
        Check(week.Segments.Any(s => s.Start == new TimeSpan(9, 40, 0)));
        // Колонки, в отличие от строк, остаются своими у каждого дня
        Check(monday.TotalColumns == 1 && week.For(DayOfWeek.Sunday).TotalColumns == 1);
        return Task.CompletedTask;
    }

    private static Task FreeDayKeepsHeight()
    {
        var week = WeekLayout.Build(
        [
            Pair(DayOfWeek.Monday, "09:00", "10:25", "ПН"),
            Pair(DayOfWeek.Tuesday, "12:10", "12:50", "ВТ короткая")
        ], []);

        var free = week.For(DayOfWeek.Friday);
        Check(free.Lessons.Count == 0);
        Check(TimelineMetrics.TotalHeight(free.RowHeights) == week.TotalHeight && week.TotalHeight > 0);
        Check(free.Segments.Count == week.Segments.Count);
        // Свободный день не должен покрыться подписями «окно»: строки заняты парами
        // других дней, и GapRows считается по всей неделе
        Check(free.GapRows.Count(gap => gap) == 1);
        return Task.CompletedTask;
    }

    private static Task BaseDayBlocks()
    {
        var week = WeekLayout.Build(
            [Pair(DayOfWeek.Monday, "09:00", "10:25", "Пара")],
            [
                new BaseDay { Day = DayOfWeek.Tuesday, AllDay = true },
                new BaseDay { Day = DayOfWeek.Wednesday, StartTime = new(13, 55, 0), EndTime = new(15, 20, 0) }
            ]);

        // Пометка со временем приносит в сетку свои границы
        Check(week.Segments.Any(s => s.Start == new TimeSpan(13, 55, 0)));

        var tuesday = week.For(DayOfWeek.Tuesday);
        Check(tuesday.Lessons.Count == 0);   // пометка не пара
        var allDay = tuesday.Markers.Single();
        Check(allDay.StartRow == 0 && allDay.RowSpan == week.Segments.Count);
        Check(allDay.Text == "Базовый день");

        var wednesday = week.For(DayOfWeek.Wednesday);
        var timed = wednesday.Markers.Single();
        Check(week.Segments[timed.StartRow].Start == new TimeSpan(13, 55, 0));
        Check(week.Segments[timed.StartRow + timed.RowSpan - 1].End == new TimeSpan(15, 20, 0));
        Check(timed.Text == "Базовый день · 13:55–15:20");

        // У понедельника пометок нет, а пара на месте
        Check(week.For(DayOfWeek.Monday).Markers.Count == 0);
        return Task.CompletedTask;
    }

    private static Task CurrentTimeMarker()
    {
        var week = WeekLayout.Build(
        [
            Pair(DayOfWeek.Monday, "09:00", "10:25", "Первая"),
            Pair(DayOfWeek.Monday, "10:35", "12:00", "Вторая")
        ], []);
        var layout = week.For(DayOfWeek.Monday);

        // 09:30 — тридцатая минута из восьмидесяти пяти в первом сегменте
        TimelineLayoutBuilder.RefreshState(layout, Monday, Monday.AddHours(9).AddMinutes(30));
        Check(Math.Abs(layout.CurrentTimeOffset!.Value - layout.RowHeights[0] * 30.0 / 85.0) < 0.001);

        // Метка ползет внутри перерыва, а не прыгает по границам пар
        TimelineLayoutBuilder.RefreshState(layout, Monday, Monday.AddHours(10).AddMinutes(30));
        double inBreak = layout.CurrentTimeOffset!.Value;
        Check(inBreak > layout.RowHeights[0] && inBreak < layout.RowHeights[0] + layout.RowHeights[1]);

        // До начала расписания и после его конца метки нет: иначе она прилипала бы
        // к краю и врала о том, где сейчас время
        TimelineLayoutBuilder.RefreshState(layout, Monday, Monday.AddHours(7));
        Check(layout.CurrentTimeOffset == null);
        TimelineLayoutBuilder.RefreshState(layout, Monday, Monday.AddHours(23));
        Check(layout.CurrentTimeOffset == null);

        // Не сегодня — метки тоже нет
        TimelineLayoutBuilder.RefreshState(layout, Monday, Tuesday.AddHours(9));
        Check(layout.CurrentTimeOffset == null);
        return Task.CompletedTask;
    }
}
