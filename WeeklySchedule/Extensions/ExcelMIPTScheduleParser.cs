using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NPOI.HSSF.UserModel;
using NPOI.HSSF.Util;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using WeeklySchedule.Models;

namespace WeeklySchedule.Extensions;

public partial class ExcelMIPTScheduleParser(ILogger<ExcelMIPTScheduleParser> logger)
{
    private readonly ILogger<ExcelMIPTScheduleParser> _logger = logger;
    private static readonly DataFormatter _formatter = new();

    #region LoggerMessage (CA1873)
    [LoggerMessage(Level = LogLevel.Information, Message = "Парсинг группы: {GroupName} из {FilePath}")]
    private static partial void LogStartParsing(ILogger logger, string groupName, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Найдено пар для {GroupName}: {Count}")]
    private static partial void LogParsingFinished(ILogger logger, string groupName, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Группа {GroupName} не найдена.")]
    private static partial void LogGroupNotFound(ILogger logger, string groupName);
    #endregion

    #region GeneratedRegex (SYSLIB1045)
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"Б\d{2}-\d{3}")]
    private static partial Regex GroupPatternRegex();

    [GeneratedRegex(@"(?=Б\d{2}-\d{3})")]
    private static partial Regex SplitGroupsRegex();

    [GeneratedRegex(@"[\s\n\r\t]+")]
    private static partial Regex CellTextSplitRegex();

    [GeneratedRegex(@"\s*[-–—]\s*")]
    private static partial Regex TimeRangeSplitRegex();

    [GeneratedRegex(@"(ауд\.|кабинет|каб\.)\s*\d+|(\d+\s+[А-Я]{1,3}\b)", RegexOptions.IgnoreCase)]
    private static partial Regex RoomMatchRegex();

    [GeneratedRegex(@"[А-Я]\.\s*[А-Я]\.\s*[А-Яа-я]+")]
    private static partial Regex InitialsMatchRegex();

    [GeneratedRegex(@"\b[А-Я]\.\s*[А-Я]\.\s*[А-Яа-яё]+")]
    private static partial Regex InitialsOnlyMatchRegex();
    #endregion

    public static List<string> ExtractAllGroupNames(string filePath)
    {
        var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var fs = File.OpenRead(filePath);
        var workbook = WorkbookFactory.Create(fs);
        var sheet = workbook.GetSheetAt(0);

        for (int r = 0; r <= Math.Min(15, sheet.LastRowNum); r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;

            foreach (var cell in row.Cells)
            {
                var text = _formatter.FormatCellValue(cell);
                if (string.IsNullOrWhiteSpace(text)) continue;

                text = WhitespaceRegex().Replace(text, " ").Trim();
                var matches = GroupPatternRegex().Matches(text);
                if (matches.Count == 0) continue;

                if (matches.Count == 1)
                {
                    groupNames.Add(text);
                }
                else
                {
                    var parts = SplitGroupsRegex().Split(text);
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

        return [.. groupNames];
    }

    public List<Lesson> ParseGroupSchedule(string filePath, string groupName)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Парсинг группы: {GroupName} из {FilePath}", groupName, Path.GetFileName(filePath));

        var lessons = new List<Lesson>();
        using var fs = File.OpenRead(filePath);
        var workbook = WorkbookFactory.Create(fs);
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

            string rawText = (_formatter.FormatCellValue(cell) ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawText)) continue;
            var lessonType = DetermineLessonTypeByColor(cell) ?? LessonType.Lab;

            var (name, description) = ParseLessonText(cell, rawText);
            if (string.IsNullOrWhiteSpace(name)) continue;

            var (startTime, endTime) = CalculateRealTimeBounds(firstRow, lastRowOfLesson, timeSlots);
            if (startTime == TimeSpan.Zero && endTime == TimeSpan.Zero) continue;

            string lessonKey = $"{currentDay}_{startTime}_{endTime}_{name}_{description}_{lessonType}";
            if (addedLessons.Add(lessonKey))
            {
                lessons.Add(new Lesson
                {
                    Name = name,
                    Description = description,
                    Type = lessonType,
                    Day = currentDay,
                    StartTime = startTime,
                    EndTime = endTime
                });
            }
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Найдено пар для {GroupName}: {Count}", groupName, lessons.Count);
        }
        return lessons;
    }

    #region Карта временных слотов
    private static List<(int FirstRow, int LastRow, TimeSpan Start, TimeSpan End)> BuildTimeSlotsMap(ISheet sheet, int startRow, int lastRow)
    {
        List<(int FirstRow, int LastRow, TimeSpan Start, TimeSpan End)> slots = [];

        for (int i = 0; i < sheet.NumMergedRegions; i++)
        {
            var cr = sheet.GetMergedRegion(i);
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

        return [.. slots.OrderBy(ts => ts.FirstRow)];
    }

    private static (TimeSpan Start, TimeSpan End) CalculateRealTimeBounds(
        int firstRow, int lastRow,
        List<(int FirstRow, int LastRow, TimeSpan Start, TimeSpan End)> timeSlots)
    {
        TimeSpan start = TimeSpan.Zero;
        TimeSpan end = TimeSpan.Zero;

        var (startFirstRow, startLastRow, startStartTime, startEndTime) = timeSlots.FirstOrDefault(ts => firstRow >= ts.FirstRow && firstRow <= ts.LastRow);
        if (startFirstRow != 0 || startLastRow != 0 || startStartTime != TimeSpan.Zero)
        {
            if (firstRow == startFirstRow)
                start = startStartTime;
            else
                start = startEndTime - TimeSpan.FromMinutes(40);
        }

        var (endFirstRow, endLastRow, endStartTime, endEndTime) = timeSlots.FirstOrDefault(ts => lastRow >= ts.FirstRow && lastRow <= ts.LastRow);
        if (endFirstRow != 0 || endLastRow != 0 || endStartTime != TimeSpan.Zero)
        {
            if (lastRow == endLastRow)
                end = endEndTime;
            else
                end = endStartTime + TimeSpan.FromMinutes(40);
        }

        return (start, end);
    }
    #endregion

    #region Вспомогательные методы
    private static int FindGroupColumn(ISheet sheet, string groupName)
    {
        for (int r = 0; r <= Math.Min(15, sheet.LastRowNum); r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;

            foreach (var cell in row.Cells)
            {
                string cellText = GetEffectiveCellText(sheet, r, cell.ColumnIndex).Trim();
                if (cellText.Equals(groupName, StringComparison.OrdinalIgnoreCase))
                    return cell.ColumnIndex;

                var parts = CellTextSplitRegex().Split(cellText);
                if (parts.Any(p => p.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
                    return cell.ColumnIndex;

                if (Regex.IsMatch(cellText, $@"(?:^|[\s\n\r\t]){Regex.Escape(groupName)}(?:$|[\s\n\r\t])", RegexOptions.IgnoreCase))
                    return cell.ColumnIndex;
            }
        }
        return -1;
    }

    private static int FindStartDataRow(ISheet sheet)
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

    private static string GetEffectiveCellText(ISheet sheet, int rowIdx, int colIdx)
    {
        for (int i = 0; i < sheet.NumMergedRegions; i++)
        {
            var cr = sheet.GetMergedRegion(i);
            if (cr.FirstRow <= rowIdx && cr.LastRow >= rowIdx &&
                cr.FirstColumn <= colIdx && cr.LastColumn >= colIdx)
            {
                var masterRow = sheet.GetRow(cr.FirstRow);
                if (masterRow == null) return string.Empty;
                var masterCell = masterRow.GetCell(cr.FirstColumn);
                if (masterCell == null) return string.Empty;
                return (_formatter.FormatCellValue(masterCell) ?? string.Empty).Trim();
            }
        }

        var row = sheet.GetRow(rowIdx);
        if (row == null) return string.Empty;
        var cell = row.GetCell(colIdx);
        if (cell == null) return string.Empty;

        return (_formatter.FormatCellValue(cell) ?? string.Empty).Trim();
    }

    private static string GetCellText(ISheet sheet, int rowIdx, int colIdx) => GetEffectiveCellText(sheet, rowIdx, colIdx);

    private static CellRangeAddress? GetMergedRegionForCell(ISheet sheet, int rowIndex, int colIndex)
    {
        for (int i = 0; i < sheet.NumMergedRegions; i++)
        {
            var cr = sheet.GetMergedRegion(i);
            if (cr.FirstRow <= rowIndex && cr.LastRow >= rowIndex &&
                cr.FirstColumn <= colIndex && cr.LastColumn >= colIndex)
            {
                return cr;
            }
        }
        return null;
    }

    private static bool IsDayOfWeekValue(string value)
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
    private static string GetCellColorHex(ICell cell)
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
                if (hex.Length == 8) return hex[2..];
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

    private static LessonType? DetermineLessonTypeByColor(ICell cell)
    {
        string hex = GetCellColorHex(cell);
        if (string.IsNullOrEmpty(hex) || hex.Length != 6) return null;

        int r = Convert.ToInt32(hex[..2], 16);
        int g = Convert.ToInt32(hex[2..4], 16);
        int b = Convert.ToInt32(hex[4..6], 16);

        const int tolerance = 30;
        bool IsClose(int r1, int g1, int b1) =>
            Math.Abs(r - r1) <= tolerance &&
            Math.Abs(g - g1) <= tolerance &&
            Math.Abs(b - b1) <= tolerance;

        if (IsClose(255, 153, 204)) return LessonType.Lecture;
        if (IsClose(0, 204, 255) || IsClose(204, 255, 255)) return LessonType.Seminar;
        if (IsClose(255, 204, 0) || IsClose(255, 255, 153)) return LessonType.Practice;
        if (IsClose(153, 204, 0) || IsClose(204, 255, 204)) return null;

        string rawText = (_formatter.FormatCellValue(cell) ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(rawText)) return LessonType.Lab;

        return null;
    }
    #endregion

    #region Время и День недели
    private static (TimeSpan Start, TimeSpan End) ParseTimeRange(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr)) return (TimeSpan.Zero, TimeSpan.Zero);
        var parts = TimeRangeSplitRegex().Split(timeStr.Trim());
        if (parts.Length < 2) return (TimeSpan.Zero, TimeSpan.Zero);
        return (ParseSingleTime(parts[0].Trim()), ParseSingleTime(parts[1].Trim()));
    }

    private static TimeSpan ParseSingleTime(string timeStr)
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

    private static DayOfWeek ParseDayOfWeek(string value)
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
    private static (string Name, string Description) ParseLessonText(ICell cell, string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return (string.Empty, string.Empty);

        int commaIndex = rawText.IndexOf(',');
        if (commaIndex > 0)
        {
            string potentialName = CleanText(rawText[..commaIndex]);
            string potentialDesc = CleanText(rawText[(commaIndex + 1)..]);
            if (!string.IsNullOrWhiteSpace(potentialName))
            {
                return (potentialName, potentialDesc);
            }
        }

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

                    string runText = fullText[start..end].Trim();
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

        int dashIndex = rawText.IndexOf(" - ");
        if (dashIndex > 0)
        {
            return (CleanText(rawText[..dashIndex]), CleanText(rawText[(dashIndex + 3)..]));
        }

        var roomMatch = RoomMatchRegex().Match(rawText);
        if (roomMatch.Success && roomMatch.Index > 10)
        {
            string beforeRoom = CleanText(rawText[..roomMatch.Index]);
            string roomAndAfter = CleanText(rawText[roomMatch.Index..]);
            var initialsMatch = InitialsMatchRegex().Match(beforeRoom);
            if (initialsMatch.Success)
            {
                string name = CleanText(beforeRoom[..initialsMatch.Index]);
                string lecturer = CleanText(beforeRoom[initialsMatch.Index..]);
                return (name, $"{lecturer}, {roomAndAfter}");
            }
            return (beforeRoom, roomAndAfter);
        }

        var initialsOnlyMatch = InitialsOnlyMatchRegex().Match(rawText);
        if (initialsOnlyMatch.Success && initialsOnlyMatch.Index > 15)
        {
            return (CleanText(rawText[..initialsOnlyMatch.Index]), CleanText(rawText[initialsOnlyMatch.Index..]));
        }

        return (CleanText(rawText), string.Empty);
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Trim().TrimEnd('-', '–', '—', ',', ' ', '\t', '\n', '\r')
                   .TrimStart('-', '–', '—', ' ', '\t', '\n', '\r');
    }
    #endregion
}