using Microsoft.Extensions.Logging;
using NPOI.HSSF.UserModel;
using NPOI.HSSF.Util;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using System.Text.RegularExpressions;
using WeeklySchedule.Models;
using WeeklySchedule.Utilities;

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
    // «1 из» проходило как «номер + корпус»: с общим IgnoreCase половина шаблона
    // \d+\s+[А-Я]{1,3} принимает и строчные буквы. Из-за этого шесть групп получали
    // название «Блок по выбору 10:», а сам предмет уезжал в описание. Корпус в
    // исходнике всегда прописными (ГК, КПМ, ЛК), поэтому регистр здесь значим;
    // словесные формы аудитории регистронезависимы через встроенный (?i:…)
    private static readonly Regex RoomRegex = new(
        @"(?i:ауд\.|кабинет|каб\.)\s*\d+|\d+\s+[А-Я]{1,3}\b",
        RegexOptions.Compiled);

    // Явное время внутри текста пары: «с 18-00 до 21-00» или «13:55-15:20».
    // Либо ключевые слова «с … до …», либо двоеточия в обеих половинах — иначе
    // временем становятся номера аудиторий («-432 ГК»), перечисления («1 из 2»)
    // и адреса вроде «Цифра №4.24»
    private static readonly Regex ExplicitTimeRegex = new(
        @"с\s*(\d{1,2})[-:.](\d{2})\s*до\s*(\d{1,2})[-:.](\d{2})"
        + @"|(\d{1,2}):(\d{2})\s*[-–—]\s*(\d{1,2}):(\d{2})",
        RegexOptions.Compiled);
    private static readonly Regex InitialsRegex = new(@"[А-Я]\.\s*[А-Я]\.\s*[А-Яа-я]+", RegexOptions.Compiled);
    private static readonly Regex InitialsWordRegex = new(@"\b[А-Я]\.\s*[А-Я]\.\s*[А-Яа-яё]+", RegexOptions.Compiled);

    // Пороги разбора текста пары: аудитория или инициалы у самого начала ячейки
    // принадлежат названию, а не описанию. Резать по ним значило бы оставить пару
    // без названия, поэтому разделитель ищется только дальше по строке
    private const int MinNameLengthBeforeRoom = 10;
    private const int MinNameLengthBeforeInitials = 15;

    // Первая строка данных, когда строку «Дни | Часы» найти не удалось: прежнее
    // поведение разбора файла МФТИ
    private const int FallbackStartDataRow = 5;

    // Цвета заливки вне палитры, встреченные за разбор. Тип пары держится только
    // на заливке, так что перекрашенный файл разъехался бы по типам без единой
    // жалобы — здесь копятся цвета для итогового предупреждения
    private readonly HashSet<string> _unknownFillColors = new(StringComparer.OrdinalIgnoreCase);

    // Объединенные ячейки листа, разложенные по строкам. Раньше здесь был плоский
    // список, и поиск региона для ячейки перебирал его целиком — а этот поиск идет
    // на каждое обращение к ячейке во вложенных циклах, то есть выходило
    // O(строки x регионы). Теперь на строку приходятся только ее собственные регионы
    private ISheet? _mergedRegionsSheet;
    private List<CellRangeAddress> _mergedRegions = [];
    private Dictionary<int, List<CellRangeAddress>> _mergedRegionsByRow = [];

    public ExcelMIPTScheduleParser(ILogger<ExcelMIPTScheduleParser> logger)
    {
        _logger = logger;
    }

    private List<CellRangeAddress> GetMergedRegions(ISheet sheet)
    {
        EnsureMergedRegions(sheet);
        return _mergedRegions;
    }

    private void EnsureMergedRegions(ISheet sheet)
    {
        if (ReferenceEquals(_mergedRegionsSheet, sheet)) return;

        var regions = new List<CellRangeAddress>(sheet.NumMergedRegions);
        for (int i = 0; i < sheet.NumMergedRegions; i++) regions.Add(sheet.GetMergedRegion(i));

        var byRow = new Dictionary<int, List<CellRangeAddress>>();
        foreach (var region in regions)
        {
            for (int row = region.FirstRow; row <= region.LastRow; row++)
            {
                if (!byRow.TryGetValue(row, out var list)) byRow[row] = list = [];
                list.Add(region);
            }
        }

        _mergedRegions = regions;
        _mergedRegionsByRow = byRow;
        _mergedRegionsSheet = sheet;
    }

    public List<string> ExtractAllGroupNames(string filePath)
    {
        var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var fs = File.OpenRead(filePath);
        using var workbook = WorkbookFactory.Create(fs);
        var sheet = workbook.GetSheetAt(0);

        // Только строка шапки. Раньше просматривались первые 16 строк, а данные
        // начинаются с четвертой: в тексте пар перечислены группы потока, и в
        // список попадали обрывки вроде «Б01-403)- 123 ГК» и «Б01-404, б01-405,»
        // — на реальном файле МФТИ семь фантомов рядом с 78 настоящими группами.
        // Если шапку найти не удалось, ведем себя как раньше: лучше список с
        // мусором, чем пустой
        int headerRow = FindHeaderRow(sheet);
        int firstScanRow = headerRow >= 0 ? headerRow : 0;
        int lastScanRow = headerRow >= 0 ? headerRow : Math.Min(15, sheet.LastRowNum);

        for (int r = firstScanRow; r <= lastScanRow; r++)
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

    public List<Lesson> ParseGroupSchedule(string filePath, string groupName, out List<BaseDay> baseDays) =>
        ParseGroupSchedule(filePath, groupName, out baseDays, out _);

    /// <param name="skippedRows">
    /// Ячейки с текстом пары, у которых не удалось определить время. Их видно в отчете
    /// об импорте: молчаливый пропуск и был причиной "иногда парсинг ломается".
    /// Каждая считается один раз, сколько бы строк и колонок группы ни накрывало
    /// ее объединение.
    /// </param>
    public List<Lesson> ParseGroupSchedule(string filePath, string groupName,
        out List<BaseDay> baseDays, out int skippedRows)
    {
        baseDays = [];
        skippedRows = 0;
        _unknownFillColors.Clear();
        _logger.LogInformation("Парсинг группы: {GroupName} из {FilePath}", groupName, Path.GetFileName(filePath));
        var lessons = new List<Lesson>();

        using var fs = File.OpenRead(filePath);
        using var workbook = WorkbookFactory.Create(fs);
        var sheet = workbook.GetSheetAt(0);

        int headerRow = FindHeaderRow(sheet);
        var groupCols = FindGroupColumns(sheet, groupName, headerRow);
        if (groupCols.First == -1)
        {
            _logger.LogWarning("Группа {GroupName} не найдена.", groupName);
            return lessons;
        }

        var (dayCol, hourCol) = FindBlockColumns(sheet, headerRow, groupCols.First);

        int startDataRow = headerRow >= 0 ? headerRow + 1 : FallbackStartDataRow;
        int lastRow = sheet.LastRowNum;

        var timeSlots = BuildTimeSlotsMap(sheet, startDataRow, lastRow, hourCol);

        // Ниже последнего слота расписания уже нет: в файле МФТИ там лежит
        // объединенная на всю ширину сноска «В расписании возможны изменения…».
        // Строкой пары она никогда не была, но попадала в цикл и с введением
        // счетчика стала давать пользователю ложное «строк с нераспознанным
        // временем: 2» — на реальном файле 106 таких у 53 групп из 78
        int lastLessonRow = timeSlots.Count > 0 ? timeSlots[^1].LastRow : lastRow;

        DayOfWeek currentDay = DayOfWeek.Monday;

        // HashSet для отслеживания уже добавленных пар (для предотвращения дубликатов)
        var addedLessons = new HashSet<string>();

        // Ячейки, у которых не удалось определить время. Именно ячейки, а не заходы
        // в цикл: одна объединенная ячейка приходит сюда на каждой своей строке и в
        // каждой колонке группы, которую накрывает, и счетчик умножался на высоту
        // объединения и число колонок. Число видит пользователь и по нему ищет
        // строки в файле
        var skippedCells = new HashSet<(int Row, int Column)>();

        for (int r = startDataRow; r <= lastLessonRow; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;

            if (ParseDayOfWeek(GetEffectiveCellText(sheet, r, dayCol)) is DayOfWeek day) currentDay = day;

            // Все колонки группы, а не только первая: в правых лежат пары подгрупп
            // и альтернативы. Повторы снимает ключ addedLessons — объединение,
            // накрывающее обе колонки, дает один и тот же ключ
            for (int groupColIndex = groupCols.First; groupColIndex <= groupCols.Last; groupColIndex++)
            {
                var mergedRegion = GetMergedRegionForCell(sheet, r, groupColIndex);

                ICell? cell = null;
                int firstRow = r;
                int lastRowOfLesson = r;
                // Координаты ячейки со значением: у объединения это его левый
                // верхний угол, один и тот же для всех строк и колонок области
                int cellRow = r;
                int cellColumn = groupColIndex;

                if (mergedRegion != null)
                {
                    firstRow = mergedRegion.FirstRow;
                    lastRowOfLesson = mergedRegion.LastRow;
                    cellRow = mergedRegion.FirstRow;
                    cellColumn = mergedRegion.FirstColumn;

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
                    var dayRegion = GetMergedRegionForCell(sheet, r, dayCol);
                    // Пометка на весь день — либо по объединению в колонке дня, либо когда
                    // время не распозналось: подпись "00:00–00:00" врала бы о границах
                    bool allDay =
                        (dayRegion != null && firstRow <= dayRegion.FirstRow && lastRowOfLesson >= dayRegion.LastRow)
                        || !LessonTimeRange.IsValid(bounds.Start, bounds.End);
                    var marker = new BaseDay
                    {
                        Day = currentDay,
                        AllDay = allDay,
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

                // Явное время в тексте важнее геометрии объединения: «Современное
                // компьютерное зрение (с 18-00 до 21-00)» уходило как 18:40–20:05,
                // потому что читалась только разметка слотов
                if (TryParseExplicitTime(normalizedText, out var textStart, out var textEnd))
                {
                    startTime = textStart;
                    endTime = textEnd;
                }

                // Условие было через &&, то есть отсекались только пары, у которых не
                // распозналась НИ ОДНА граница. Пара с одной границей уходила в хранилище
                // с EndTime = 00:00, а WeekLayout отбрасывает такие пары — на экране
                // пропадал целый день
                if (!LessonTimeRange.IsValid(startTime, endTime))
                {
                    if (skippedCells.Add((cellRow, cellColumn)))
                    {
                        skippedRows++;
                        _logger.LogWarning(
                            "Строка {Row} ({Day}, «{Name}») пропущена: время не определено ({Start}–{End})",
                            cellRow, currentDay, name, startTime, endTime);
                    }
                    continue;
                }

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
        }

        if (_unknownFillColors.Count > 0)
        {
            _logger.LogWarning(
                "Цвета заливки вне палитры: {Colors}. Пары в таких ячейках помечены как лабораторные",
                string.Join(", ", _unknownFillColors.Order()));
        }

        _logger.LogInformation("Найдено пар для {GroupName}: {Count}, пропущено строк: {Skipped}",
            groupName, lessons.Count, skippedRows);
        return lessons;
    }

    #region Карта временных слотов

    private List<(int FirstRow, int LastRow, TimeSpan Start, TimeSpan End)> BuildTimeSlotsMap(
        ISheet sheet, int startRow, int lastRow, int hourCol)
    {
        var slots = new List<(int FirstRow, int LastRow, TimeSpan Start, TimeSpan End)>();
        var covered = new HashSet<int>();

        foreach (var cr in GetMergedRegions(sheet))
        {
            if (cr.FirstColumn != hourCol || cr.LastColumn != hourCol) continue;
            if (ParseTimeRange(GetEffectiveCellText(sheet, cr.FirstRow, hourCol)) is not { } slot) continue;

            slots.Add((cr.FirstRow, cr.LastRow, slot.Start, slot.End));
            for (int r = cr.FirstRow; r <= cr.LastRow; r++) covered.Add(r);
        }

        // Раньше принадлежность строки уже найденному слоту проверялась перебором
        // всего списка слотов на каждую строку листа
        for (int r = startRow; r <= lastRow; r++)
        {
            if (covered.Contains(r)) continue;

            if (ParseTimeRange(GetEffectiveCellText(sheet, r, hourCol)) is { } slot) slots.Add((r, r, slot.Start, slot.End));
        }

        return slots.OrderBy(ts => ts.FirstRow).ToList();
    }

    /// <summary>
    /// Время пары по строкам, которые занимает ее объединенная ячейка: границы
    /// прилипают к границам слотов из колонки "Часы".
    ///
    /// Прилипание, а не пропорциональный пересчет внутри слота: объединения в
    /// исходнике регулярно на строку выше или ниже размеченного слота, и
    /// интерполяция превращала эту неаккуратность в время, которого в расписании
    /// нет вовсе — 14:37, 16:12, 11:17. На реальном файле МФТИ так получались
    /// 67 пар из 1646, у половины групп. Каждая такая граница вдобавок становится
    /// строкой общей сетки недели и разъезжается по всем семи дням.
    ///
    /// Слот попадает в пару, если пара покрывает больше его половины. Если такого
    /// слота нет (пара уже слота), берем слот с наибольшим перекрытием: время
    /// целого слота — все равно лучшая догадка, чем его доля.
    /// </summary>
    private (TimeSpan Start, TimeSpan End) CalculateRealTimeBounds(
        int firstRow, int lastRow,
        List<(int FirstRow, int LastRow, TimeSpan Start, TimeSpan End)> timeSlots)
    {
        int startIndex = -1, endIndex = -1;
        int bestIndex = -1, bestOverlap = 0;

        // Список отсортирован по FirstRow, поэтому первый и последний подошедшие
        // слоты и есть внешние границы пары
        for (int i = 0; i < timeSlots.Count; i++)
        {
            var slot = timeSlots[i];
            int overlap = Math.Min(lastRow, slot.LastRow) - Math.Max(firstRow, slot.FirstRow) + 1;
            if (overlap <= 0) continue;

            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                bestIndex = i;
            }

            int slotRows = slot.LastRow - slot.FirstRow + 1;
            if (overlap * 2 <= slotRows) continue;

            if (startIndex < 0) startIndex = i;
            endIndex = i;
        }

        if (startIndex < 0) startIndex = endIndex = bestIndex;
        if (startIndex < 0) return (TimeSpan.Zero, TimeSpan.Zero);

        return (timeSlots[startIndex].Start, timeSlots[endIndex].End);
    }

    #endregion

    #region Вспомогательные методы

    /// <summary>
    /// Диапазон колонок группы. Шапка группы бывает объединена на несколько колонок,
    /// и под каждой лежат свои пары: у Б09-401 шапка занимает колонки 106–107, в левой
    /// стоит иностранный на 9:00 и 10:35, в правой — ещё два на 12:10 и 13:55. Читалась
    /// только левая, и приложение показывало 3 пары вместо 5.
    /// (-1, -1), если группы на листе нет.
    /// </summary>
    private (int First, int Last) FindGroupColumns(ISheet sheet, string groupName, int headerRow)
    {
        // Тот же диапазон строк, что и у ExtractAllGroupNames: список групп для
        // выбора и поиск колонки обязаны понимать «шапку» одинаково, иначе
        // выбранная группа не найдется при разборе
        int firstScanRow = headerRow >= 0 ? headerRow : 0;
        int lastScanRow = headerRow >= 0 ? headerRow : Math.Min(15, sheet.LastRowNum);

        for (int r = firstScanRow; r <= lastScanRow; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            foreach (var cell in row.Cells)
            {
                string cellText = GetEffectiveCellText(sheet, r, cell.ColumnIndex).Trim();
                if (!MatchesGroup(cellText, groupName)) continue;

                var region = GetMergedRegionForCell(sheet, r, cell.ColumnIndex);
                return region == null
                    ? (cell.ColumnIndex, cell.ColumnIndex)
                    : (region.FirstColumn, region.LastColumn);
            }
        }
        return (-1, -1);
    }

    private static bool MatchesGroup(string cellText, string groupName)
    {
        // Список для выбора собирается из НОРМАЛИЗОВАННОГО текста ячейки
        // (ExtractAllGroupNames), поэтому сравнивать надо тоже нормализованный.
        // Иначе шапка «Б05-411\nЦУ» предлагается пользователю как «Б05-411 ЦУ»,
        // а здесь не находится ни одним из трёх способов: точное сравнение
        // спотыкается о перенос против пробела, разбиение по словам дает части
        // порознь, а regex ищет имя с настоящим пробелом внутри. Ячейки с
        // несколькими группами это не задевало, их спасало разбиение по словам
        cellText = WhitespaceRegex.Replace(cellText, " ").Trim();

        // 1. Точное совпадение (быстрый путь)
        if (cellText.Equals(groupName, StringComparison.OrdinalIgnoreCase)) return true;

        // 2. Ячейка содержит несколько групп через перенос/пробел (например "Б09-401\nБ09-402")
        // Разбиваем по переносам строк и пробелам, ищем точное совпадение одной из частей
        var parts = WordSplitRegex.Split(cellText);
        if (parts.Any(p => p.Equals(groupName, StringComparison.OrdinalIgnoreCase))) return true;

        // 3. Fallback: regex-поиск с границами слова (на случай если группа встроена в текст)
        return Regex.IsMatch(cellText,
            $@"(?:^|[\s\n\r\t]){Regex.Escape(groupName)}(?:$|[\s\n\r\t])", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Колонки «Дни» и «Часы» того блока, в котором лежит группа. На листе МФТИ
    /// десять таблиц бок о бок, у каждой своя пара таких колонок, и времена у них
    /// не совпадают: в среду колонка B говорит «1840 - 2005», а AE — «1845 - 2005».
    /// День и время брались всегда из колонок 0 и 1, поэтому группы неголовных
    /// блоков получали чужое время.
    /// Если пары колонок слева не нашлось, возвращаем (0, 1) — прежнее поведение.
    /// </summary>
    private (int DayCol, int HourCol) FindBlockColumns(ISheet sheet, int headerRow, int groupCol)
    {
        if (headerRow < 0) return (0, 1);

        int dayCol = -1, hourCol = -1;
        // Берем ближайшую слева пару: блоки идут «Дни | Часы | группы… | Дни | Часы | …»
        for (int c = 1; c <= groupCol; c++)
        {
            if (GetEffectiveCellText(sheet, headerRow, c) == "Часы" &&
                GetEffectiveCellText(sheet, headerRow, c - 1) == "Дни")
            {
                dayCol = c - 1;
                hourCol = c;
            }
        }

        return hourCol >= 0 ? (dayCol, hourCol) : (0, 1);
    }

    /// <summary>
    /// Строка с названиями групп: та, где в первых двух колонках стоят «Дни» и «Часы».
    /// -1, если такой строки нет.
    /// </summary>
    private int FindHeaderRow(ISheet sheet)
    {
        for (int r = 0; r <= Math.Min(20, sheet.LastRowNum); r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            string col1 = GetEffectiveCellText(sheet, r, 0);
            string col2 = GetEffectiveCellText(sheet, r, 1);
            if (col1 == "Дни" && col2 == "Часы") return r;
        }
        return -1;
    }

    /// <summary>
    /// Явное время из текста пары. false, если его там нет или оно бессмысленно —
    /// тогда время берется из геометрии объединения, как раньше.
    /// </summary>
    private static bool TryParseExplicitTime(string text, out TimeSpan start, out TimeSpan end)
    {
        start = end = TimeSpan.Zero;

        var m = ExplicitTimeRegex.Match(text);
        if (!m.Success) return false;

        // Две альтернативы шаблона: группы 1-4 для «с … до …», 5-8 для «13:55-15:20»
        int g = m.Groups[1].Success ? 1 : 5;
        if (!int.TryParse(m.Groups[g].Value, out int h1) ||
            !int.TryParse(m.Groups[g + 1].Value, out int m1) ||
            !int.TryParse(m.Groups[g + 2].Value, out int h2) ||
            !int.TryParse(m.Groups[g + 3].Value, out int m2)) return false;

        if (h1 > 23 || h2 > 23 || m1 > 59 || m2 > 59) return false;

        start = new TimeSpan(h1, m1, 0);
        end = new TimeSpan(h2, m2, 0);

        // Тем же предикатом, что и остальные границы: обратный или нулевой
        // интервал лучше отдать геометрии, чем сохранить
        return LessonTimeRange.IsValid(start, end);
    }

    /// <summary>
    /// Текст ячейки с учетом объединений: у объединенной области значение хранится
    /// только в левой верхней ячейке.
    /// </summary>
    private string GetEffectiveCellText(ISheet sheet, int rowIdx, int colIdx)
    {
        var region = GetMergedRegionForCell(sheet, rowIdx, colIdx);
        if (region != null)
        {
            var masterRow = sheet.GetRow(region.FirstRow);
            var masterCell = masterRow?.GetCell(region.FirstColumn);
            return masterCell == null ? string.Empty : _formatter.FormatCellValue(masterCell).Trim();
        }

        var row = sheet.GetRow(rowIdx);
        var cell = row?.GetCell(colIdx);
        return cell == null ? string.Empty : _formatter.FormatCellValue(cell).Trim();
    }

    private CellRangeAddress? GetMergedRegionForCell(ISheet sheet, int rowIndex, int colIndex)
    {
        EnsureMergedRegions(sheet);
        if (!_mergedRegionsByRow.TryGetValue(rowIndex, out var regions)) return null;

        foreach (var cr in regions)
        {
            if (cr.FirstColumn <= colIndex && cr.LastColumn >= colIndex) return cr;
        }
        return null;
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
        if (string.IsNullOrWhiteSpace(rawText)) return null;

        _unknownFillColors.Add(hex);
        return LessonType.Lab;
    }

    #endregion

    #region Время и День недели

    /// <summary>Границы слота из колонки «Часы». null, если прочитать их не удалось.</summary>
    private static (TimeSpan Start, TimeSpan End)? ParseTimeRange(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr)) return null;
        var parts = TimeRangeSplitRegex.Split(timeStr.Trim());
        if (parts.Length < 2) return null;
        if (ParseSingleTime(parts[0]) is not TimeSpan start) return null;
        if (ParseSingleTime(parts[1]) is not TimeSpan end) return null;
        return (start, end);
    }

    /// <summary>
    /// «900», «9:00», «9.00» → время дня. Дефис здесь не снимается: он разделяет
    /// границы слота, и его убирает ParseTimeRange раньше. null, если прочитать не
    /// удалось: здесь стоял TimeSpan.Zero, и нераспознанное время было не отличить
    /// от полуночи — ошибка молча превращалась в правдоподобные данные.
    /// </summary>
    private static TimeSpan? ParseSingleTime(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr)) return null;
        timeStr = timeStr.Replace(" ", "").Replace(":", "").Replace(".", "");
        if (timeStr.Length == 3) timeStr = "0" + timeStr;
        if (timeStr.Length == 4 && int.TryParse(timeStr, out int minutes))
        {
            int hours = minutes / 100;
            int mins = minutes % 100;
            if (hours >= 0 && hours <= 23 && mins >= 0 && mins <= 59)
                return new TimeSpan(hours, mins, 0);
        }
        return null;
    }

    /// <summary>
    /// День недели из колонки «Дни». null, если это не подпись дня: метод отдавал
    /// понедельник на любой текст, и отличить его от настоящего понедельника было
    /// нельзя — приходилось держать рядом вторую проверку с тем же списком слов.
    /// </summary>
    private static DayOfWeek? ParseDayOfWeek(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string lower = value.ToLowerInvariant().Trim();
        if (lower.Contains("понедельник") || lower == "пн") return DayOfWeek.Monday;
        if (lower.Contains("вторник") || lower == "вт") return DayOfWeek.Tuesday;
        if (lower.Contains("среда") || lower == "ср") return DayOfWeek.Wednesday;
        if (lower.Contains("четверг") || lower == "чт") return DayOfWeek.Thursday;
        if (lower.Contains("пятница") || lower == "пт") return DayOfWeek.Friday;
        if (lower.Contains("суббота") || lower == "сб") return DayOfWeek.Saturday;
        if (lower.Contains("воскресенье") || lower == "вс") return DayOfWeek.Sunday;
        return null;
    }

    #endregion

    #region Логика парсинга текста

    public (string Name, string Description) ParseLessonText(ICell cell, string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return (string.Empty, string.Empty);

        // Сценарий 0: Разделение по первой запятой вне скобок
        int commaIndex = IndexOfTopLevelComma(rawText);
        if (commaIndex > 0)
        {
            string potentialName = CleanText(rawText.Substring(0, commaIndex));
            string potentialDesc = CleanText(rawText.Substring(commaIndex + 1));
            if (!string.IsNullOrWhiteSpace(potentialName))
            {
                return (potentialName, potentialDesc);
            }
        }

        // Сценарий 1: разметка шрифтом внутри ячейки
        if (cell.CellType == CellType.String && TrySplitByBoldRuns(cell) is { } byFont) return byFont;

        // Сценарий 2: Разделитель " - "
        int dashIndex = rawText.IndexOf(" - ");
        if (dashIndex > 0)
        {
            return (CleanText(rawText.Substring(0, dashIndex)), CleanText(rawText.Substring(dashIndex + 3)));
        }

        // Сценарий 3: Поиск номера аудитории
        var roomMatch = RoomRegex.Match(rawText);
        if (roomMatch.Success && roomMatch.Index > MinNameLengthBeforeRoom)
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
        if (initialsOnlyMatch.Success && initialsOnlyMatch.Index > MinNameLengthBeforeInitials)
        {
            return (CleanText(rawText.Substring(0, initialsOnlyMatch.Index)), CleanText(rawText.Substring(initialsOnlyMatch.Index)));
        }

        // Fallback
        return (CleanText(rawText), string.Empty);
    }

    /// <summary>
    /// Делит текст ячейки по разметке шрифтом: жирное начало — название пары,
    /// остальное — описание. null, если ячейка размечена одним шрифтом или
    /// жирного начала в ней нет, — тогда работают сценарии ниже.
    ///
    /// Прогоны форматирования одинаковы у обоих форматов книги, а вот шрифт
    /// прогона NPOI отдает по-разному, и разобран был только .xls. В .xlsx —
    /// а именно его и присылают — приписка кафедры оставалась внутри названия.
    /// </summary>
    private static (string Name, string Description)? TrySplitByBoldRuns(ICell cell)
    {
        var rich = cell.RichStringCellValue;
        if (rich == null || rich.NumFormattingRuns <= 1) return null;

        var nameParts = new List<string>();
        var descParts = new List<string>();
        bool isDescription = false;

        for (int i = 0; i < rich.NumFormattingRuns; i++)
        {
            int start = rich.GetIndexOfFormattingRun(i);
            int end = i + 1 < rich.NumFormattingRuns ? rich.GetIndexOfFormattingRun(i + 1) : rich.Length;
            if (start < 0 || end <= start) continue;

            string runText = rich.String.Substring(start, end - start).Trim();
            if (string.IsNullOrEmpty(runText)) continue;

            if (!isDescription && IsBoldRun(cell, rich, i)) nameParts.Add(runText);
            else
            {
                isDescription = true;
                descParts.Add(runText);
            }
        }

        if (nameParts.Count == 0 || descParts.Count == 0) return null;
        return (CleanText(string.Join(" ", nameParts)), CleanText(string.Join(" ", descParts)));
    }

    /// <summary>
    /// Жирный ли прогон форматирования. В .xls шрифт прогона — индекс в книге,
    /// в .xlsx — сам шрифт; отсутствие шрифта у прогона означает шрифт ячейки.
    /// </summary>
    private static bool IsBoldRun(ICell cell, IRichTextString rich, int runIndex) => rich switch
    {
        HSSFRichTextString hssf =>
            cell.Sheet.Workbook.GetFontAt(hssf.GetFontOfFormattingRun(runIndex))?.IsBold == true,
        XSSFRichTextString xssf => xssf.GetFontOfFormattingRun(runIndex)?.IsBold == true,
        _ => false
    };

    /// <summary>
    /// Первая запятая вне скобок, -1 если такой нет. Резать по любой первой запятой
    /// нельзя: в исходнике сплошь перечисления в скобках, и «Прикладная статистика
    /// (МТС, Декарт)» разрывалось на «…(МТС» и «Декарт)».
    /// </summary>
    private static int IndexOfTopLevelComma(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            // Не уходим в минус: лишняя закрывающая скобка не должна открывать
            // запятым дорогу обратно
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (c == ',' && depth == 0) return i;
        }
        return -1;
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Trim().TrimEnd('-', '–', '—', ',', ' ', '\t', '\n', '\r')
                   .TrimStart('-', '–', '—', ' ', '\t', '\n', '\r');
    }

    #endregion
}
