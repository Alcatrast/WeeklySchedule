using Microsoft.Extensions.Logging;
using NPOI.HSSF.UserModel;
using NPOI.HSSF.Util;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using System.Text.RegularExpressions;
using WeeklySchedule.Models;

namespace WeeklySchedule.Extensions;

public class ExcelMIPTScheduleParser
{
    private readonly ILogger<ExcelMIPTScheduleParser> _logger;
    private readonly DataFormatter _formatter = new();

    // Регулярки компилируются один раз, а не на каждую ячейку
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex GroupNameRegex = new(@"Б\d{2}-\d{3}", RegexOptions.Compiled);
    private static readonly Regex GroupNameSplitRegex = new(@"(?=Б\d{2}-\d{3})", RegexOptions.Compiled);
    private static readonly Regex WordSplitRegex = new(@"[\s\n\r\t]+", RegexOptions.Compiled);
    private static readonly Regex TimeRangeSplitRegex = new(@"\s*[-–—]\s*", RegexOptions.Compiled);
    private static readonly Regex RoomRegex = new(
        @"(ауд\.|кабинет|каб\.)\s*\d+|(\d+\s+[А-Я]{1,3}\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InitialsRegex = new(@"[А-Я]\.\s*[А-Я]\.\s*[А-Яа-я]+", RegexOptions.Compiled);
    private static readonly Regex InitialsWordRegex = new(@"\b[А-Я]\.\s*[А-Я]\.\s*[А-Яа-яё]+", RegexOptions.Compiled);

    // Объединенные ячейки листа. Раньше NumMergedRegions/GetMergedRegion дергались
    // на каждое обращение к ячейке во вложенных циклах
    private ISheet? _mergedRegionsSheet;
    private List<CellRangeAddress> _mergedRegions = [];

    public ExcelMIPTScheduleParser(ILogger<ExcelMIPTScheduleParser> logger)
    {
        _logger = logger;
    }

    private List<CellRangeAddress> GetMergedRegions(ISheet sheet)
    {
        if (!ReferenceEquals(_mergedRegionsSheet, sheet))
        {
            var regions = new List<CellRangeAddress>(sheet.NumMergedRegions);
            for (int i = 0; i < sheet.NumMergedRegions; i++) regions.Add(sheet.GetMergedRegion(i));
            _mergedRegions = regions;
            _mergedRegionsSheet = sheet;
        }
        return _mergedRegions;
    }

    public List<string> ExtractAllGroupNames(string filePath)
    {
        var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var fs = File.OpenRead(filePath);
        using var workbook = WorkbookFactory.Create(fs);
        var sheet = workbook.GetSheetAt(0);

        for (int r = 0; r <= Math.Min(15, sheet.LastRowNum); r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            foreach (var cell in row.Cells)
            {
                var text = _formatter.FormatCellValue(cell);
                if (string.IsNullOrWhiteSpace(text)) continue;

                // Нормализуем пробелы и переносы строк
                text = WhitespaceRegex.Replace(text, " ").Trim();

                // Ищем все вхождения паттерна группы
                var matches = GroupNameRegex.Matches(text);
                if (matches.Count == 0) continue;

                if (matches.Count == 1)
                {
                    // Одно вхождение — берем весь текст ячейки как название группы
                    // (включая суффиксы типа "ЦУ", "КПМ" и т.д.)
                    groupNames.Add(text);
                }
                else
                {
                    // Несколько вхождений — разделяем по паттерну
                    // Например: "Б09-401 Б09-402"
                    var parts = GroupNameSplitRegex.Split(text);
                    foreach (var part in parts)
                    {
                        var trimmed = part.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmed))
                        {
                            groupNames.Add(trimmed);
                        }
                    }
                }
            }
        }
        return groupNames.ToList();
    }

    public List<Lesson> ParseGroupSchedule(string filePath, string groupName)
        => ParseGroupSchedule(filePath, groupName, out _);

    public List<Lesson> ParseGroupSchedule(string filePath, string groupName, out List<BaseDay> baseDays)
    {
        baseDays = [];
        _logger.LogInformation("Парсинг группы: {GroupName} из {FilePath}", groupName, Path.GetFileName(filePath));
        var lessons = new List<Lesson>();

        using var fs = File.OpenRead(filePath);
        using var workbook = WorkbookFactory.Create(fs);
        var sheet = workbook.GetSheetAt(0);

        int groupColIndex = FindGroupColumn(sheet, groupName);
        if (groupColIndex == -1)
        {
            _logger.LogWarning("Группа {GroupName} не найдена.", groupName);
            return lessons;
        }

        int startDataRow = FindStartDataRow(sheet);
        int lastRow = sheet.LastRowNum;

        var timeSlots = BuildTimeSlotsMap(sheet, startDataRow, lastRow);

        DayOfWeek currentDay = DayOfWeek.Monday;

        // HashSet для отслеживания уже добавленных пар (для предотвращения дубликатов)
        var addedLessons = new HashSet<string>();

        for (int r = startDataRow; r <= lastRow; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;

            string dayStr = GetEffectiveCellText(sheet, r, 0);
            if (!string.IsNullOrWhiteSpace(dayStr) && IsDayOfWeekValue(dayStr))
            {
                currentDay = ParseDayOfWeek(dayStr);
            }

            var mergedRegion = GetMergedRegionForCell(sheet, r, groupColIndex);

            ICell? cell = null;
            int firstRow = r;
            int lastRowOfLesson = r;

            if (mergedRegion != null)
            {
                firstRow = mergedRegion.FirstRow;
                lastRowOfLesson = mergedRegion.LastRow;

                var masterRow = sheet.GetRow(mergedRegion.FirstRow);
                if (masterRow != null)
                {
                    cell = masterRow.GetCell(mergedRegion.FirstColumn);
                }
            }
            else
            {
                cell = row.GetCell(groupColIndex);
            }

            if (cell == null || cell.CellType == CellType.Blank) continue;

            string rawText = _formatter.FormatCellValue(cell).Trim();
            if (string.IsNullOrWhiteSpace(rawText)) continue;

            var normalizedText = WhitespaceRegex.Replace(rawText, " ").Trim();
            if (normalizedText.StartsWith("Базовый день", StringComparison.OrdinalIgnoreCase))
            {
                var bounds = CalculateRealTimeBounds(firstRow, lastRowOfLesson, timeSlots);
                var dayRegion = GetMergedRegionForCell(sheet, r, 0);
                var marker = new BaseDay
                {
                    Day = currentDay,
                    AllDay = dayRegion != null && firstRow <= dayRegion.FirstRow && lastRowOfLesson >= dayRegion.LastRow,
                    StartTime = bounds.Start,
                    EndTime = bounds.End,
                    Text = "Базовый день" + normalizedText["Базовый день".Length..]
                };
                if (!baseDays.Contains(marker)) baseDays.Add(marker);
                continue;
            }

            // null означает "не пара" (зеленая заливка) — такие ячейки пропускаем.
            // Нераспознанные цвета метод сам отдает как LessonType.Lab.
            var lessonType = DetermineLessonTypeByColor(cell);
            if (lessonType == null) continue;

            var (name, description) = ParseLessonText(cell, rawText);
            if (string.IsNullOrWhiteSpace(name)) continue;

            var (startTime, endTime) = CalculateRealTimeBounds(firstRow, lastRowOfLesson, timeSlots);
            if (startTime == TimeSpan.Zero && endTime == TimeSpan.Zero) continue;

            // Создаем уникальный ключ для проверки дубликатов
            string lessonKey = $"{currentDay}_{startTime}_{endTime}_{name}_{description}_{lessonType}";

            if (!addedLessons.Contains(lessonKey))
            {
                addedLessons.Add(lessonKey);
                lessons.Add(new Lesson
                {
                    Name = name,
                    Description = description,
                    Type = lessonType.Value,
                    Day = currentDay,
                    StartTime = startTime,
                    EndTime = endTime
                });
            }
        }

        _logger.LogInformation("Найдено пар для {GroupName}: {Count}", groupName, lessons.Count);
        return lessons;
    }

    #region Карта временных слотов

    private List<(int FirstRow, int LastRow, TimeSpan Start, TimeSpan End)> BuildTimeSlotsMap(ISheet sheet, int startRow, int lastRow)
    {
        var slots = new List<(int FirstRow, int LastRow, TimeSpan Start, TimeSpan End)>();

        foreach (var cr in GetMergedRegions(sheet))
        {
            if (cr.FirstColumn == 1 && cr.LastColumn == 1)
            {
                var text = GetEffectiveCellText(sheet, cr.FirstRow, 1);
                var (s, e) = ParseTimeRange(text);
                if (s != TimeSpan.Zero && e != TimeSpan.Zero)
                {
                    slots.Add((cr.FirstRow, cr.LastRow, s, e));
                }
            }
        }

        for (int r = startRow; r <= lastRow; r++)
        {
            bool covered = slots.Any(ts => r >= ts.FirstRow && r <= ts.LastRow);
            if (!covered)
            {
                var text = GetCellText(sheet, r, 1);
                var (s, e) = ParseTimeRange(text);
                if (s != TimeSpan.Zero && e != TimeSpan.Zero)
                {
                    slots.Add((r, r, s, e));
                }
            }
        }

        return slots.OrderBy(ts => ts.FirstRow).ToList();
    }

    private (TimeSpan Start, TimeSpan End) CalculateRealTimeBounds(
        int firstRow, int lastRow,
        List<(int FirstRow, int LastRow, TimeSpan Start, TimeSpan End)> timeSlots)
    {
        TimeSpan start = TimeSpan.Zero;
        TimeSpan end = TimeSpan.Zero;

        // Раньше "слот найден" проверялось сравнением полей кортежа с нулем: слот,
        // который начинается в первой строке листа, считался бы ненайденным
        int startIndex = timeSlots.FindIndex(ts => firstRow >= ts.FirstRow && firstRow <= ts.LastRow);
        if (startIndex >= 0)
        {
            var slot = timeSlots[startIndex];
            start = firstRow == slot.FirstRow
                ? slot.Start
                : InterpolateSlotTime(slot, firstRow - slot.FirstRow);
        }

        int endIndex = timeSlots.FindIndex(ts => lastRow >= ts.FirstRow && lastRow <= ts.LastRow);
        if (endIndex >= 0)
        {
            var slot = timeSlots[endIndex];
            end = lastRow == slot.LastRow
                ? slot.End
                : InterpolateSlotTime(slot, lastRow - slot.FirstRow + 1);
        }

        return (start, end);
    }

    /// <summary>
    /// Время границы пары, которая занимает слот не целиком: делим слот на строки
    /// пропорционально. Для обычной пары (слот из двух строк по 80 минут) это дает
    /// ровно те 40 минут, которые раньше были захардкожены, но не ломается на
    /// слотах другой длины и другого числа строк.
    /// </summary>
    private static TimeSpan InterpolateSlotTime(
        (int FirstRow, int LastRow, TimeSpan Start, TimeSpan End) slot, int rowOffset)
    {
        int rows = slot.LastRow - slot.FirstRow + 1;
        if (rows <= 0) return slot.Start;
        return slot.Start + (slot.End - slot.Start) * rowOffset / rows;
    }

    #endregion

    #region Вспомогательные методы

    private int FindGroupColumn(ISheet sheet, string groupName)
    {
        for (int r = 0; r <= Math.Min(15, sheet.LastRowNum); r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            foreach (var cell in row.Cells)
            {
                string cellText = GetEffectiveCellText(sheet, r, cell.ColumnIndex).Trim();

                // 1. Точное совпадение (быстрый путь)
                if (cellText.Equals(groupName, StringComparison.OrdinalIgnoreCase))
                    return cell.ColumnIndex;

                // 2. Ячейка содержит несколько групп через перенос/пробел (например "Б09-401\nБ09-402")
                // Разбиваем по переносам строк и пробелам, ищем точное совпадение одной из частей
                var parts = WordSplitRegex.Split(cellText);
                if (parts.Any(p => p.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
                    return cell.ColumnIndex;

                // 3. Fallback: regex-поиск с границами слова (на случай если группа встроена в текст)
                if (Regex.IsMatch(cellText, $@"(?:^|[\s\n\r\t]){Regex.Escape(groupName)}(?:$|[\s\n\r\t])", RegexOptions.IgnoreCase))
                    return cell.ColumnIndex;
            }
        }
        return -1;
    }

    private int FindStartDataRow(ISheet sheet)
    {
        for (int r = 0; r <= Math.Min(20, sheet.LastRowNum); r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            string col1 = GetEffectiveCellText(sheet, r, 0);
            string col2 = GetEffectiveCellText(sheet, r, 1);
            if (col1 == "Дни" && col2 == "Часы") return r + 1;
        }
        return 5;
    }

    private string GetEffectiveCellText(ISheet sheet, int rowIdx, int colIdx)
    {
        foreach (var cr in GetMergedRegions(sheet))
        {
            if (cr.FirstRow <= rowIdx && cr.LastRow >= rowIdx &&
                cr.FirstColumn <= colIdx && cr.LastColumn >= colIdx)
            {
                var masterRow = sheet.GetRow(cr.FirstRow);
                if (masterRow == null) return string.Empty;
                var masterCell = masterRow.GetCell(cr.FirstColumn);
                if (masterCell == null) return string.Empty;
                return _formatter.FormatCellValue(masterCell).Trim();
            }
        }

        var row = sheet.GetRow(rowIdx);
        if (row == null) return string.Empty;
        var cell = row.GetCell(colIdx);
        if (cell == null) return string.Empty;
        return _formatter.FormatCellValue(cell).Trim();
    }

    private string GetCellText(ISheet sheet, int rowIdx, int colIdx) => GetEffectiveCellText(sheet, rowIdx, colIdx);

    private CellRangeAddress? GetMergedRegionForCell(ISheet sheet, int rowIndex, int colIndex)
    {
        foreach (var cr in GetMergedRegions(sheet))
        {
            if (cr.FirstRow <= rowIndex && cr.LastRow >= rowIndex &&
                cr.FirstColumn <= colIndex && cr.LastColumn >= colIndex)
            {
                return cr;
            }
        }
        return null;
    }

    private bool IsDayOfWeekValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string lower = value.ToLowerInvariant();
        return lower.Contains("понедельник") || lower.Contains("вторник") ||
               lower.Contains("среда") || lower.Contains("четверг") ||
               lower.Contains("пятница") || lower.Contains("суббота") ||
               lower.Contains("воскресенье") ||
               lower == "пн" || lower == "вт" || lower == "ср" ||
               lower == "чт" || lower == "пт" || lower == "сб" || lower == "вс";
    }

    #endregion

    #region Цвет и тип пары
    private string GetCellColorHex(ICell cell)
    {
        var style = cell.CellStyle;
        if (style == null) return "FFFFFF";

        IColor? color = style.FillForegroundColorColor;
        if (color == null || color.Indexed == IndexedColors.Automatic.Index)
        {
            color = style.FillBackgroundColorColor;
        }

        if (color == null) return "FFFFFF";

        if (color is HSSFColor hssfColor)
        {
            var triplet = hssfColor.GetTriplet();
            return $"{triplet[0]:X2}{triplet[1]:X2}{triplet[2]:X2}";
        }
        else if (color is XSSFColor xssfColor)
        {
            var hex = xssfColor.ARGBHex;
            if (!string.IsNullOrEmpty(hex))
            {
                if (hex.Length == 8) return hex.Substring(2);
                if (hex.Length == 6) return hex;
            }
            var rgb = xssfColor.RGB;
            if (rgb != null && rgb.Length >= 3)
            {
                return $"{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}";
            }
        }

        return "FFFFFF";
    }
    /// <summary>
    /// Определяет тип пары сравнением цвета ячейки с окрестностями эталонных цветов палитры.
    /// Допуск: ±30 по каждому RGB-каналу.
    /// </summary>
    private LessonType? DetermineLessonTypeByColor(ICell cell)
    {
        string hex = GetCellColorHex(cell);
        if (string.IsNullOrEmpty(hex) || hex.Length != 6) return null;

        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);

        const int tolerance = 30;

        bool IsClose(int r1, int g1, int b1) =>
            Math.Abs(r - r1) <= tolerance &&
            Math.Abs(g - g1) <= tolerance &&
            Math.Abs(b - b1) <= tolerance;

        // FF99CC (255,153,204) — Лекция (розовый)
        if (IsClose(255, 153, 204)) return LessonType.Lecture;

        // 00CCFF (0,204,255) и CCFFFF (204,255,255) — Семинар (голубой/светло-голубой)
        if (IsClose(0, 204, 255) || IsClose(204, 255, 255)) return LessonType.Seminar;

        // FFCC00 (255,204,0) и FFFF99 (255,255,153) — Практика (жёлтый/светло-жёлтый)
        if (IsClose(255, 204, 0) || IsClose(255, 255, 153)) return LessonType.Practice;

        // 99CC00 (153,204,0) и CCFFCC (204,255,204) — Не пара (зелёный/светло-зелёный)
        if (IsClose(153, 204, 0) || IsClose(204, 255, 204)) return null;

        // Fallback: если цвет не распознан, но ячейка содержит текст — считаем Лабораторной
        string rawText = _formatter.FormatCellValue(cell).Trim();
        if (!string.IsNullOrWhiteSpace(rawText)) return LessonType.Lab;

        return null;
    }

    #endregion

    #region Время и День недели

    private (TimeSpan Start, TimeSpan End) ParseTimeRange(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr)) return (TimeSpan.Zero, TimeSpan.Zero);
        var parts = TimeRangeSplitRegex.Split(timeStr.Trim());
        if (parts.Length < 2) return (TimeSpan.Zero, TimeSpan.Zero);
        return (ParseSingleTime(parts[0].Trim()), ParseSingleTime(parts[1].Trim()));
    }

    private TimeSpan ParseSingleTime(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr)) return TimeSpan.Zero;
        timeStr = timeStr.Replace(" ", "").Replace(":", "").Replace(".", "");
        if (timeStr.Length == 3) timeStr = "0" + timeStr;
        if (timeStr.Length == 4 && int.TryParse(timeStr, out int minutes))
        {
            int hours = minutes / 100;
            int mins = minutes % 100;
            if (hours >= 0 && hours <= 23 && mins >= 0 && mins <= 59)
                return new TimeSpan(hours, mins, 0);
        }
        return TimeSpan.Zero;
    }

    private DayOfWeek ParseDayOfWeek(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DayOfWeek.Monday;
        string lower = value.ToLowerInvariant().Trim();
        if (lower.Contains("понедельник") || lower == "пн") return DayOfWeek.Monday;
        if (lower.Contains("вторник") || lower == "вт") return DayOfWeek.Tuesday;
        if (lower.Contains("среда") || lower == "ср") return DayOfWeek.Wednesday;
        if (lower.Contains("четверг") || lower == "чт") return DayOfWeek.Thursday;
        if (lower.Contains("пятница") || lower == "пт") return DayOfWeek.Friday;
        if (lower.Contains("суббота") || lower == "сб") return DayOfWeek.Saturday;
        if (lower.Contains("воскресенье") || lower == "вс") return DayOfWeek.Sunday;
        return DayOfWeek.Monday;
    }

    #endregion

    #region Логика парсинга текста

    public (string Name, string Description) ParseLessonText(ICell cell, string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return (string.Empty, string.Empty);

        // Сценарий 0: Разделение по первой запятой
        int commaIndex = rawText.IndexOf(',');
        if (commaIndex > 0)
        {
            string potentialName = CleanText(rawText.Substring(0, commaIndex));
            string potentialDesc = CleanText(rawText.Substring(commaIndex + 1));
            if (!string.IsNullOrWhiteSpace(potentialName))
            {
                return (potentialName, potentialDesc);
            }
        }

        // Сценарий 1: RichText
        if (cell.CellType == CellType.String && cell.RichStringCellValue is HSSFRichTextString hssfRts)
        {
            if (hssfRts.NumFormattingRuns > 1)
            {
                var nameParts = new List<string>();
                var descParts = new List<string>();
                bool isDescription = false;
                var workbook = cell.Sheet.Workbook;
                string fullText = hssfRts.String;

                for (int i = 0; i < hssfRts.NumFormattingRuns; i++)
                {
                    int start = hssfRts.GetIndexOfFormattingRun(i);
                    int end = (i + 1 < hssfRts.NumFormattingRuns) ? hssfRts.GetIndexOfFormattingRun(i + 1) : hssfRts.Length;
                    string runText = fullText.Substring(start, end - start).Trim();
                    if (string.IsNullOrEmpty(runText)) continue;

                    short fontIndex = hssfRts.GetFontOfFormattingRun(i);
                    var font = workbook.GetFontAt(fontIndex);
                    bool isBold = font != null && font.IsBold;

                    if (!isDescription && isBold) nameParts.Add(runText);
                    else
                    {
                        isDescription = true;
                        descParts.Add(runText);
                    }
                }

                if (nameParts.Count > 0 && descParts.Count > 0)
                    return (CleanText(string.Join(" ", nameParts)), CleanText(string.Join(" ", descParts)));
            }
        }

        // Сценарий 2: Разделитель " - "
        int dashIndex = rawText.IndexOf(" - ");
        if (dashIndex > 0)
        {
            return (CleanText(rawText.Substring(0, dashIndex)), CleanText(rawText.Substring(dashIndex + 3)));
        }

        // Сценарий 3: Поиск номера аудитории
        var roomMatch = RoomRegex.Match(rawText);
        if (roomMatch.Success && roomMatch.Index > 10)
        {
            string beforeRoom = CleanText(rawText.Substring(0, roomMatch.Index));
            string roomAndAfter = CleanText(rawText.Substring(roomMatch.Index));
            var initialsMatch = InitialsRegex.Match(beforeRoom);
            if (initialsMatch.Success)
            {
                string name = CleanText(beforeRoom.Substring(0, initialsMatch.Index));
                string lecturer = CleanText(beforeRoom.Substring(initialsMatch.Index));
                return (name, $"{lecturer}, {roomAndAfter}");
            }
            return (beforeRoom, roomAndAfter);
        }

        // Сценарий 4: Поиск инициалов
        var initialsOnlyMatch = InitialsWordRegex.Match(rawText);
        if (initialsOnlyMatch.Success && initialsOnlyMatch.Index > 15)
        {
            return (CleanText(rawText.Substring(0, initialsOnlyMatch.Index)), CleanText(rawText.Substring(initialsOnlyMatch.Index)));
        }

        // Fallback
        return (CleanText(rawText), string.Empty);
    }

    private string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Trim().TrimEnd('-', '–', '—', ',', ' ', '\t', '\n', '\r')
                   .TrimStart('-', '–', '—', ' ', '\t', '\n', '\r');
    }

    #endregion
}
