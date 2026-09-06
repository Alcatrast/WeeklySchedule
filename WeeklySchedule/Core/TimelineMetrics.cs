using WeeklySchedule.Models;

namespace WeeklySchedule.Core;

// Единый масштаб «минуты → dp» для всех дней. Раньше DayView считал масштаб заново
// для каждой страницы от самой короткой пары именно этого дня, поэтому одна и та же
// пара 09:00–10:25 занимала разную высоту в понедельник и во вторник.
//
// Здесь не должно появляться типов MAUI: файл целиком уезжает в тестовый проект
// через <Compile Include="../WeeklySchedule/Core/*.cs">.
public static class TimelineMetrics
{
    public const double PixelsPerMinute = 1.1;   // 85-минутная пара → 94 dp
    public const double MinLessonHeight = 64;    // короткая пара остается читаемой
    public const double MaxLessonHeight = 320;
    public const double MinGapHeight = 8;
    public const double MaxGapHeight = 48;       // «окно» на два часа не съедает экран
    public const int GapLabelThreshold = 30;     // от скольких минут подписывать окно

    public static double LessonHeight(int minutes) =>
        Math.Clamp(minutes * PixelsPerMinute, MinLessonHeight, MaxLessonHeight);

    public static double GapHeight(int minutes) =>
        Math.Clamp(minutes * PixelsPerMinute, MinGapHeight, MaxGapHeight);

    // Строка сетки — это сегмент, а не пара: пара может занимать несколько сегментов,
    // а в одном сегменте могут лежать несколько параллельных пар.
    public static double[] RowHeights(TimelineLayout layout)
    {
        var segments = layout.Segments;
        var heights = new double[segments.Count];
        if (heights.Length == 0) return heights;

        for (int i = 0; i < heights.Length; i++)
            heights[i] = IsGapRow(layout, i) ? GapHeight(segments[i].DurationMinutes) : 0;

        // Целевая высота пары зависит только от ее длительности. Раскладываем ее по
        // строкам пропорционально сегментам, а занятая строка берет максимум из долей
        // всех пар, которые ее покрывают: тогда сумма строк пары равна ее высоте
        // (для одиночной пары точно, для параллельных — не меньше).
        foreach (var placement in layout.Lessons)
        {
            int startRow = Math.Clamp(placement.StartRow, 0, heights.Length - 1);
            int endRow = Math.Clamp(startRow + Math.Max(1, placement.RowSpan) - 1, startRow, heights.Length - 1);
            int spanMinutes = 0;
            for (int i = startRow; i <= endRow; i++) spanMinutes += segments[i].DurationMinutes;
            if (spanMinutes <= 0) continue;

            double target = LessonHeight(placement.TotalMinutes);
            for (int i = startRow; i <= endRow; i++)
            {
                double share = target * segments[i].DurationMinutes / spanMinutes;
                if (share > heights[i]) heights[i] = share;
            }
        }
        return heights;
    }

    // Смещение верха строки от начала сетки. Отдельно от SpanHeight: там пустой
    // диапазон означает «одна строка», а здесь — ноль.
    public static double TopOffset(double[] rows, int row)
    {
        double total = 0;
        for (int i = 0; i < Math.Min(rows.Length, Math.Max(0, row)); i++) total += rows[i];
        return total;
    }

    public static double SpanHeight(double[] rows, int startRow, int rowSpan)
    {
        double total = 0;
        for (int i = Math.Max(0, startRow); i < Math.Min(rows.Length, startRow + Math.Max(1, rowSpan)); i++)
            total += rows[i];
        return total;
    }

    public static bool IsGapRow(TimelineLayout layout, int rowIndex) =>
        !layout.Lessons.Any(p => p.StartRow <= rowIndex && rowIndex < p.StartRow + Math.Max(1, p.RowSpan));

    public static string FormatGap(int minutes)
    {
        string prefix = minutes >= GapLabelThreshold ? "окно" : "перерыв";
        int hours = minutes / 60;
        int rest = minutes % 60;
        if (hours == 0) return $"{prefix} {rest} мин";
        return rest == 0 ? $"{prefix} {hours} ч" : $"{prefix} {hours} ч {rest} мин";
    }
}
