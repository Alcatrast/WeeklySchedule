using Microsoft.Extensions.Logging.Abstractions;
using WeeklySchedule.Core;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Extensions;
using WeeklySchedule.Models;
using WeeklySchedule.Services;

static class ImportSourceRegression
{
    public static (string, Func<Task>)[] Tests =>
    [
        ("Imported file is copied into app storage and survives a cache wipe", SourceIsKept),
        ("Reimport replaces imported lessons and keeps manual ones", ReimportReplacesImported),
        ("Missing group does not wipe the schedule", MissingGroupKeepsLessons),
        ("Empty parse result does not wipe the schedule", EmptyParseKeepsLessons),
        ("Reimport without a stored source reports it instead of clearing", NoSourceIsReported),
        ("Lesson spanning past its time slot keeps a usable end time", SpanningLessonKeepsItsEnd),
        ("Rows without a readable time are skipped, not stored half-parsed", RowsWithoutTimeAreSkipped),
        ("Reimport clears lessons left unrenderable by an older parse", UnrenderableLessonsAreCleared),
        ("A merge overhanging the next slot snaps to slot bounds", MergeOverhangSnapsToSlotBounds),
        ("The sheet footer is not reported as a row with an unreadable time", FooterIsNotCountedAsSkipped),
        ("Group names mentioned inside lessons stay out of the group list", GroupListTakesOnlyTheHeaderRow),
        ("A lesson name is not cut at a comma inside brackets", NameKeepsBracketedList)
    ];

    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }

    private static NullLogger<ExcelMIPTScheduleParser> Logger => NullLogger<ExcelMIPTScheduleParser>.Instance;

    /// <summary>
    /// Лист в том же формате, что читает парсер: дни в колонке 0, часы в колонке 1,
    /// дальше по колонке на группу.
    /// </summary>
    private static string WriteWorkbook(string name, string group, string secondPairTime)
    {
        using var workbook = new NPOI.HSSF.UserModel.HSSFWorkbook();
        var sheet = workbook.CreateSheet("Schedule");
        void Cell(int row, int column, string text) =>
            (sheet.GetRow(row) ?? sheet.CreateRow(row)).CreateCell(column).SetCellValue(text);

        Cell(0, 0, "Дни"); Cell(0, 1, "Часы"); Cell(0, 2, group);
        Cell(1, 0, "Понедельник");
        Cell(1, 1, "900 - 1025"); Cell(1, 2, "Матанализ");
        Cell(2, 1, secondPairTime); Cell(2, 2, "Физика");
        sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(1, 2, 0, 0));

        var path = Path.Combine(FileSystem.AppDataDirectory, name);
        using var stream = File.Create(path);
        workbook.Write(stream);
        return path;
    }

    private static async Task<(Repository Repo, Timeline Timeline)> ImportedTimelineAsync(string secondPairTime = "1035 - 1200")
    {
        var repo = new Repository();
        var timeline = new Timeline { Name = "Б03-401" };
        repo.Timelines.Add(timeline);

        var picked = WriteWorkbook("picked.xls", "Б03-401", secondPairTime);
        // Копия делается ровно так же, как в HandleImportAsync: из потока файла
        using (var stream = File.OpenRead(picked))
            await ScheduleSourceStore.SaveAsync(timeline.Id, stream);

        var parser = new ExcelMIPTScheduleParser(Logger);
        var lessons = parser.ParseGroupSchedule(ScheduleSourceStore.PathFor(timeline.Id), "Б03-401", out var baseDays);
        Check(lessons.Count == 2);
        foreach (var lesson in lessons) lesson.FromImport = true;
        await LessonImportService.AddMissingAsync(repo, timeline.Id, lessons);
        timeline.BaseDays = baseDays;
        timeline.Source = new ImportSource
        {
            FileName = "raspisanie.xls", GroupName = "Б03-401", ImportedAt = DateTime.Now
        };
        return (repo, timeline);
    }

    // Исходная поломка: разбор шел по пути от системного пикера, а он ведет во
    // временную папку. GroupSelectionPage перечитывает файл на каждом появлении,
    // и после чистки кэша возврат из фона давал «Ошибка при чтении файла».
    private static async Task SourceIsKept()
    {
        var (_, timeline) = await ImportedTimelineAsync();
        var picked = Path.Combine(FileSystem.AppDataDirectory, "picked.xls");

        Check(ScheduleSourceStore.Exists(timeline.Id));
        File.Delete(picked);   // система вычистила кэш пикера
        Check(ScheduleSourceStore.Exists(timeline.Id));

        var parser = new ExcelMIPTScheduleParser(Logger);
        Check(parser.ExtractAllGroupNames(ScheduleSourceStore.PathFor(timeline.Id)).Contains("Б03-401"));

        // Копия лежит внутри папки таймлайна, чтобы уходить вместе с ним
        Check(ScheduleSourceStore.PathFor(timeline.Id).Contains(timeline.Id.ToString()));
        ScheduleSourceStore.DiscardOrphan(timeline.Id);
        Check(!ScheduleSourceStore.Exists(timeline.Id));
    }

    private static async Task ReimportReplacesImported()
    {
        var (repo, timeline) = await ImportedTimelineAsync();
        var manual = new Lesson
        {
            TimelineId = timeline.Id, Name = "Дедлайн", Day = DayOfWeek.Monday,
            StartTime = new TimeSpan(18, 0, 0), EndTime = new TimeSpan(19, 0, 0)
        };
        repo.Lessons.Add(manual);

        // В файле поменялось время второй пары
        var updated = WriteWorkbook("updated.xls", "Б03-401", "1100 - 1225");
        using (var stream = File.OpenRead(updated))
            await ScheduleSourceStore.SaveAsync(timeline.Id, stream);

        var result = await ScheduleReimportService.ReimportAsync(repo, repo, timeline, Logger);
        Check(result.Status == ReimportStatus.Success);
        Check(result.Parsed == 2 && result.Removed == 2 && result.Added == 2);

        var stored = repo.Lessons.Where(l => l.TimelineId == timeline.Id).ToList();
        // Ручная пара на месте, устаревшая импортированная — нет
        Check(stored.Any(l => l.Id == manual.Id && !l.FromImport));
        Check(!stored.Any(l => l.FromImport && l.StartTime == new TimeSpan(10, 35, 0)));
        Check(stored.Count(l => l.FromImport && l.StartTime == new TimeSpan(11, 0, 0)) == 1);
        Check(stored.Count == 3);
    }

    // Парсер на ненайденной группе молча отдает пустой список: без отдельной
    // проверки повторный разбор вычистил бы расписание и ничего не сказал.
    private static async Task MissingGroupKeepsLessons()
    {
        var (repo, timeline) = await ImportedTimelineAsync();
        var other = WriteWorkbook("other.xls", "Б05-999", "1035 - 1200");
        using (var stream = File.OpenRead(other))
            await ScheduleSourceStore.SaveAsync(timeline.Id, stream);

        var result = await ScheduleReimportService.ReimportAsync(repo, repo, timeline, Logger);
        Check(result.Status == ReimportStatus.GroupNotFound);
        Check(repo.Lessons.Count(l => l.TimelineId == timeline.Id) == 2 && repo.Deletions == 0);
    }

    private static async Task EmptyParseKeepsLessons()
    {
        var (repo, timeline) = await ImportedTimelineAsync();
        // Группа в шапке есть, а строк с парами нет
        using (var workbook = new NPOI.HSSF.UserModel.HSSFWorkbook())
        {
            var sheet = workbook.CreateSheet("Schedule");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("Дни");
            header.CreateCell(1).SetCellValue("Часы");
            header.CreateCell(2).SetCellValue("Б03-401");
            var path = Path.Combine(FileSystem.AppDataDirectory, "headers.xls");
            using (var file = File.Create(path)) workbook.Write(file);
            using var stream = File.OpenRead(path);
            await ScheduleSourceStore.SaveAsync(timeline.Id, stream);
        }

        var result = await ScheduleReimportService.ReimportAsync(repo, repo, timeline, Logger);
        Check(result.Status == ReimportStatus.NoLessons);
        Check(repo.Lessons.Count(l => l.TimelineId == timeline.Id) == 2 && repo.Deletions == 0);
    }

    private static async Task NoSourceIsReported()
    {
        var repo = new Repository();
        // Расписание, заведенное до появления сохраняемой копии
        var legacy = new Timeline { Name = "Старое" };
        repo.Timelines.Add(legacy);
        repo.Lessons.Add(new Lesson { TimelineId = legacy.Id, Name = "Пара" });

        var result = await ScheduleReimportService.ReimportAsync(repo, repo, legacy, Logger);
        Check(result.Status == ReimportStatus.NoSource);
        Check(repo.Lessons.Count == 1);

        // Метаданные есть, а файла нет — тот же ответ, а не пустое расписание
        legacy.Source = new ImportSource { FileName = "x.xls", GroupName = "Б03-401" };
        result = await ScheduleReimportService.ReimportAsync(repo, repo, legacy, Logger);
        Check(result.Status == ReimportStatus.NoSource && repo.Lessons.Count == 1);
    }

    /// <summary>
    /// Лист, на котором терялись дни: объединенная ячейка пары занимает две строки,
    /// а время стоит только в верхней. Для нижней строки слота нет.
    /// </summary>
    private static string WriteWorkbookWithSpanningLesson(string name, string group)
    {
        using var workbook = new NPOI.HSSF.UserModel.HSSFWorkbook();
        var sheet = workbook.CreateSheet("Schedule");
        void Cell(int row, int column, string text) =>
            (sheet.GetRow(row) ?? sheet.CreateRow(row)).CreateCell(column).SetCellValue(text);

        Cell(0, 0, "Дни"); Cell(0, 1, "Часы"); Cell(0, 2, group);
        Cell(1, 0, "Вторник");
        Cell(1, 1, "900 - 1025"); Cell(1, 2, "Матанализ");
        Cell(2, 1, "");                                  // "Часы" в нижней строке пары пустые
        Cell(3, 1, "1035 - 1200"); Cell(3, 2, "Физика");
        sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(1, 3, 0, 0));   // день
        sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(1, 2, 2, 2));   // пара на две строки

        var path = Path.Combine(FileSystem.AppDataDirectory, name);
        using var stream = File.Create(path);
        workbook.Write(stream);
        return path;
    }

    // Раньше конец такой пары оставался нулевым: проверка отсекала только строки, где
    // не распозналась ни одна граница. Пара с EndTime = 00:00 доезжала до хранилища,
    // а сетка ее отбрасывала — на экране пропадал целый день
    private static Task SpanningLessonKeepsItsEnd()
    {
        var path = WriteWorkbookWithSpanningLesson("spanning.xls", "Б03-401");
        var parser = new ExcelMIPTScheduleParser(Logger);
        var lessons = parser.ParseGroupSchedule(path, "Б03-401", out _, out int skipped);

        Check(skipped == 0);
        Check(lessons.Count == 2);
        var math = lessons.Single(l => l.Name == "Матанализ");
        Check(math.Day == DayOfWeek.Tuesday);
        Check(math.StartTime == new TimeSpan(9, 0, 0) && math.EndTime == new TimeSpan(10, 25, 0));

        // Главное: пары действительно попадают в сетку, а не отсеиваются на входе
        var week = WeekLayout.Build(lessons, []);
        Check(week.For(DayOfWeek.Tuesday).Lessons.Count == 2);
        return Task.CompletedTask;
    }

    private static Task RowsWithoutTimeAreSkipped()
    {
        using (var workbook = new NPOI.HSSF.UserModel.HSSFWorkbook())
        {
            var sheet = workbook.CreateSheet("Schedule");
            void Cell(int row, int column, string text) =>
                (sheet.GetRow(row) ?? sheet.CreateRow(row)).CreateCell(column).SetCellValue(text);

            Cell(0, 0, "Дни"); Cell(0, 1, "Часы"); Cell(0, 2, "Б03-401");
            Cell(1, 0, "Среда"); Cell(1, 1, "весь день"); Cell(1, 2, "Матанализ");

            using var file = File.Create(Path.Combine(FileSystem.AppDataDirectory, "notime.xls"));
            workbook.Write(file);
        }

        var parser = new ExcelMIPTScheduleParser(Logger);
        var lessons = parser.ParseGroupSchedule(
            Path.Combine(FileSystem.AppDataDirectory, "notime.xls"), "Б03-401", out _, out int skipped);

        // Пропуск считается и попадает в отчет об импорте: молчаливый был причиной
        // «парсинг иногда ломается, и непонятно почему»
        Check(lessons.Count == 0 && skipped == 1);
        return Task.CompletedTask;
    }

    private static async Task UnrenderableLessonsAreCleared()
    {
        var (repo, timeline) = await ImportedTimelineAsync();
        // Осадок прошлого разбора: показать нельзя, признака импорта нет, поэтому
        // сама по себе такая пара не удалилась бы никогда
        repo.Lessons.Add(new Lesson
        {
            TimelineId = timeline.Id, Name = "Матанализ", Day = DayOfWeek.Monday,
            StartTime = new TimeSpan(9, 0, 0), EndTime = TimeSpan.Zero
        });

        var result = await ScheduleReimportService.ReimportAsync(repo, repo, timeline, Logger);
        Check(result.Status == ReimportStatus.Success && result.Repaired == 1);

        var stored = repo.Lessons.Where(l => l.TimelineId == timeline.Id).ToList();
        Check(stored.Count == 2);
        Check(stored.All(l => l.EndTime > l.StartTime));
        // И день снова виден целиком
        Check(WeekLayout.Build(stored, []).For(DayOfWeek.Monday).Lessons.Count == 2);
    }

    /// <summary>
    /// Лист в форме настоящего файла МФТИ: слоты в колонке «Часы» объединены по две
    /// строки, объединения пар местами на строку выше или ниже слота, внизу листа —
    /// сноска во всю ширину.
    /// </summary>
    private static string WriteWorkbookLikeMipt(string name, string group)
    {
        using var workbook = new NPOI.HSSF.UserModel.HSSFWorkbook();
        var sheet = workbook.CreateSheet("Schedule");
        void Cell(int row, int column, string text) =>
            (sheet.GetRow(row) ?? sheet.CreateRow(row)).CreateCell(column).SetCellValue(text);
        void Merge(int r1, int r2, int c1, int c2) =>
            sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(r1, r2, c1, c2));

        Cell(0, 0, "Дни"); Cell(0, 1, "Часы"); Cell(0, 2, group);
        Cell(1, 0, "Понедельник"); Merge(1, 6, 0, 0);

        Cell(1, 1, "900 - 1025");  Merge(1, 2, 1, 1);
        Cell(3, 1, "1035 - 1200"); Merge(3, 4, 1, 1);
        Cell(5, 1, "1210 - 1335"); Merge(5, 6, 1, 1);

        // Свисает на строку в следующий слот: настоящее время — 900-1025
        Cell(1, 2, "Матанализ"); Merge(1, 3, 2, 2);
        // Начинается на строку раньше своего слота: настоящее время — 1210-1335
        Cell(4, 2, "Физика (лекция, семинар)"); Merge(4, 6, 2, 2);

        Cell(7, 1, "В расписании возможны изменения, актуальную версию см. на сайте "
                 + "https://mipt.ru/institute/departments/education_department/schedule");
        Merge(7, 8, 1, 2);

        var path = Path.Combine(FileSystem.AppDataDirectory, name);
        using var stream = File.Create(path);
        workbook.Write(stream);
        return path;
    }

    // Интерполяция доли слота выдавала время, которого в расписании нет вовсе —
    // 14:37, 16:12, 11:17. На реальном файле так получались 67 пар из 1646.
    // Каждая такая граница вдобавок становится строкой общей сетки недели
    private static Task MergeOverhangSnapsToSlotBounds()
    {
        var path = WriteWorkbookLikeMipt("mipt.xls", "Б03-401");
        var parser = new ExcelMIPTScheduleParser(Logger);
        var lessons = parser.ParseGroupSchedule(path, "Б03-401", out _, out _);

        Check(lessons.Count == 2);
        var math = lessons.Single(l => l.Name.StartsWith("Матанализ"));
        Check(math.StartTime == new TimeSpan(9, 0, 0) && math.EndTime == new TimeSpan(10, 25, 0));
        var physics = lessons.Single(l => l.Name.StartsWith("Физика"));
        Check(physics.StartTime == new TimeSpan(12, 10, 0) && physics.EndTime == new TimeSpan(13, 35, 0));

        // Ни одной лишней строки в сетке: только границы настоящих слотов
        var segments = WeekLayout.Build(lessons, []).Segments;
        Check(segments.All(s => s.Start.Minutes is 0 or 25 or 35 or 10));
        return Task.CompletedTask;
    }

    // Сноска внизу листа парой никогда не была, но попадала в цикл и давала
    // пользователю ложное «строк с нераспознанным временем: 2»
    private static Task FooterIsNotCountedAsSkipped()
    {
        var path = WriteWorkbookLikeMipt("footer.xls", "Б03-401");
        var parser = new ExcelMIPTScheduleParser(Logger);
        parser.ParseGroupSchedule(path, "Б03-401", out _, out int skipped);
        Check(skipped == 0);
        return Task.CompletedTask;
    }

    // Список групп собирался по первым 16 строкам, а данные начинаются с четвертой:
    // из текста пары про поток в список приезжали обрывки вроде «Б03-403)- 123 ГК»
    private static Task GroupListTakesOnlyTheHeaderRow()
    {
        using (var workbook = new NPOI.HSSF.UserModel.HSSFWorkbook())
        {
            var sheet = workbook.CreateSheet("Schedule");
            void Cell(int row, int column, string text) =>
                (sheet.GetRow(row) ?? sheet.CreateRow(row)).CreateCell(column).SetCellValue(text);

            Cell(0, 0, "Дни"); Cell(0, 1, "Часы"); Cell(0, 2, "Б03-401");
            Cell(1, 0, "Понедельник"); Cell(1, 1, "900 - 1025");
            Cell(1, 2, "Теория поля, доцент Гец А.В. (Б03-402, Б03-403)- 123 ГК");

            using var file = File.Create(Path.Combine(FileSystem.AppDataDirectory, "groups.xls"));
            workbook.Write(file);
        }

        var parser = new ExcelMIPTScheduleParser(Logger);
        var groups = parser.ExtractAllGroupNames(Path.Combine(FileSystem.AppDataDirectory, "groups.xls"));
        Check(groups.Count == 1 && groups[0] == "Б03-401");
        return Task.CompletedTask;
    }

    // Резать по первой попавшейся запятой нельзя: в исходнике сплошь перечисления
    // в скобках, и «Прикладная статистика (МТС, Декарт)» разрывалось пополам
    private static Task NameKeepsBracketedList()
    {
        var path = WriteWorkbookLikeMipt("brackets.xls", "Б03-401");
        var parser = new ExcelMIPTScheduleParser(Logger);
        var lessons = parser.ParseGroupSchedule(path, "Б03-401", out _, out _);

        var physics = lessons.Single(l => l.Name.StartsWith("Физика"));
        Check(physics.Name == "Физика (лекция, семинар)" && physics.Description.Length == 0);
        return Task.CompletedTask;
    }

    private sealed class Repository : ILessonRepository, ITimelineRepository
    {
        public List<Lesson> Lessons { get; } = [];
        public List<Timeline> Timelines { get; } = [];
        public int Deletions;

        Task<IEnumerable<Lesson>> ILessonRepository.GetAllAsync() => Task.FromResult<IEnumerable<Lesson>>(Lessons.ToList());
        public Task<IEnumerable<Lesson>> GetByTimelineIdAsync(Guid id) =>
            Task.FromResult<IEnumerable<Lesson>>(Lessons.Where(l => l.TimelineId == id).ToList());
        Task<Lesson?> ILessonRepository.GetByIdAsync(Guid id) => Task.FromResult(Lessons.FirstOrDefault(l => l.Id == id));
        public Task AddAsync(Lesson lesson) { Lessons.Add(lesson); return Task.CompletedTask; }
        public Task UpdateAsync(Lesson lesson) => Task.CompletedTask;
        Task ILessonRepository.DeleteAsync(Guid id) { Deletions++; Lessons.RemoveAll(l => l.Id == id); return Task.CompletedTask; }
        public Task DeleteManyAsync(Guid timelineId, IEnumerable<Guid> ids)
        {
            var set = ids.ToHashSet();
            Deletions += Lessons.RemoveAll(l => l.TimelineId == timelineId && set.Contains(l.Id));
            return Task.CompletedTask;
        }

        public Task<IEnumerable<Timeline>> GetAllAsync() => Task.FromResult<IEnumerable<Timeline>>(Timelines.ToList());
        Task<Timeline?> ITimelineRepository.GetByIdAsync(Guid id) => Task.FromResult(Timelines.FirstOrDefault(t => t.Id == id));
        public Task AddAsync(Timeline timeline) { Timelines.Add(timeline); return Task.CompletedTask; }
        public Task UpdateAsync(Timeline timeline) => Task.CompletedTask;
        Task ITimelineRepository.DeleteAsync(Guid id) { Timelines.RemoveAll(t => t.Id == id); return Task.CompletedTask; }
        public Task<bool> TryRecoverCorruptedAsync() => Task.FromResult(false);
    }
}
