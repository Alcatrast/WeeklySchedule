using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;
using NPOI.HSSF.UserModel;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Extensions;
using WeeklySchedule.Messaging;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.ViewModels;

static class ImportSafetyRegression
{
    public static (string, Func<Task>)[] Tests =>
    [
        Test("Cancelling a picked file preserves the committed source and lessons", CancelPreservesSource),
        Test("Declining replacement preserves source, metadata and lessons", DeclinePreservesSource),
        Test("Partial reimport preserves every previous lesson and metadata", PartialReimportPreserves),
        Test("Partial picked-file import never publishes its source or lessons", PartialPickedImportPreserves),
        Test("Retry after failed creation persists the new group and source", RetryUpdatesSource),
        Test("Empty retry after failed creation preserves the stored lessons", EmptyRetryPreserves),
        Test("A locked old lesson stops replacement before any write", LockedReadStopsReplacement),
        Test("Corrupt and mismatched lesson files stop replacement but remain on disk", CorruptReadStopsReplacement),
        Test("Failed picked-file import refreshes changed data and preserves old source", FailedWriteNotifies),
        Test("Failed reimport refreshes changed data", FailedReimportNotifies),
        Test("Failed deletion during reimport refreshes changed data", FailedDeletionNotifies),
        Test("Failed metadata commit retains the previous source and refreshes lessons", FailedMetadataKeepsSource),
        Test("Successful import survives restart and cancellation of the next attempt", CommittedSourceSurvives),
        Test("Cached timeline lists refresh the source used by editors", CachedSourceRefreshes),
        Test("Blocked import names the unreadable lesson without changing data", UnreadableLessonIsExplained),
        Test("Unreadable picked file discards only its staged copy", UnreadablePickedFilePreserves)
    ];
    static (string, Func<Task>) Test(string name, Func<Task> run) => (name, async () =>
    {
        Application.Current = new Application();
        Application.Current.Windows.Add(new Window { Page = new Page() });
        Shell.Current = null;
        await run();
    });
    static Page CurrentPage => Application.Current!.Windows[0].Page!;
    static readonly NullLogger<ExcelMIPTScheduleParser> Log = NullLogger<ExcelMIPTScheduleParser>.Instance;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Task Call(object vm, string name, params object[] args) =>
        (Task)vm.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, args)!;
    static string Workbook(string name, bool badMiddle = false)
    {
        using var book = new HSSFWorkbook();
        var sheet = book.CreateSheet("Schedule");
        void Cell(int r, int c, string text) => (sheet.GetRow(r) ?? sheet.CreateRow(r)).CreateCell(c).SetCellValue(text);
        Cell(0, 0, "Дни"); Cell(0, 1, "Часы"); Cell(0, 2, "Б03-401"); Cell(0, 3, "Б03-402"); Cell(0, 4, "Б03-403");
        var slots = new[] { "900 - 1025", badMiddle ? "весь день" : "1035 - 1200", "1220 - 1345" };
        for (int r = 1; r <= 3; r++)
        {
            Cell(r, 0, "Понедельник"); Cell(r, 1, slots[r - 1]);
            Cell(r, 2, "ГруппаА Предмет" + r); Cell(r, 3, "ГруппаБ Предмет" + r);
        }
        Cell(4, 0, "Вторник"); Cell(4, 1, "900 - 1025"); Cell(4, 2, "Базовый день кафедры А");
        Cell(5, 0, "Среда"); Cell(5, 1, "900 - 1025"); Cell(5, 3, "Базовый день кафедры Б");
        var path = Path.Combine(FileSystem.AppDataDirectory, name);
        using var file = File.Create(path); book.Write(file); return path;
    }
    static async Task SaveSource(Timeline timeline, string path)
    {
        using var stream = File.OpenRead(path);
        await ScheduleSourceStore.SaveAsync(timeline.Id, stream);
    }
    static Timeline NewTimeline() => new() { Name = "Review", Source = new() { FileName = "old.xls", GroupName = "Б03-401" } };
    static async Task<string> Stage(Timeline t, bool badMiddle = false)
    {
        using var stream = File.OpenRead(Workbook("picked.xls", badMiddle));
        return await ScheduleSourceStore.StageAsync(t.Id, stream);
    }
    static GroupSelectionViewModel GroupVm(Timeline t, ILessonRepository lessons, ITimelineRepository timelines, bool exists, string path) =>
        new(path, "picked.xls", exists, t, lessons, timelines, new Nav(), new Provider());
    static Task Import(GroupSelectionViewModel vm, string group) => Call(vm, "ImportGroupAsync", new GroupItem { FullGroupName = group });
    static async Task CancelPreservesSource()
    {
        var t = NewTimeline(); var tr = new FileTimelineRepository(); var lr = new FileLessonRepository();
        await SaveSource(t, Workbook("old.xls")); await tr.AddAsync(t);
        var before = File.ReadAllBytes(ScheduleSourceStore.PathFor(t.Id));
        await lr.AddAsync(Lesson(t.Id));
        var chosen = Workbook("different.xls", true);
        var vm = new EditTimelineViewModel(tr, lr, new TestSettings(), null!, new Picker(chosen), new Nav(), null, t);
        string? pending = null;
        vm.ImportRequested += (path, _, _, _) => pending = path;
        await Call(vm, "HandleImportAsync");
        Check(pending != null && File.Exists(pending), "picked file was not staged");
        File.Delete(chosen); // Кэш пикера может исчезнуть до выбора группы.
        GroupVm(t, lr, tr, true, pending!).DiscardUnfinishedSource();
        var source = File.ReadAllBytes(ScheduleSourceStore.PathFor(t.Id));
        Check(source.SequenceEqual(before), "cancel changed committed source");
        Check(!File.Exists(pending), "cancel left staged source");
        Check((await tr.GetByIdAsync(t.Id))!.Source!.FileName == "old.xls", "metadata unexpectedly updated");
        Check((await lr.GetByTimelineIdAsync(t.Id)).Count() == 1, "cancel deleted lessons");
    }
    static async Task DeclinePreservesSource()
    {
        var t = NewTimeline(); var tr = new FileTimelineRepository(); var lr = new FileLessonRepository();
        await SaveSource(t, Workbook("old.xls")); await tr.AddAsync(t); await lr.AddAsync(Lesson(t.Id));
        var before = File.ReadAllBytes(ScheduleSourceStore.PathFor(t.Id));
        var pending = await Stage(t);
        var vm = GroupVm(t, lr, tr, true, pending);
        CurrentPage.ConfirmationResult = false;
        await Import(vm, "Б03-402");
        Check(CurrentPage.Confirmations == 1, "replace did not ask");
        Check((await lr.GetByTimelineIdAsync(t.Id)).Single().Name == "Same lesson", "decline replaced lessons");
        Check((await tr.GetByIdAsync(t.Id))!.Source!.GroupName == "Б03-401", "decline replaced group");
        Check(File.ReadAllBytes(ScheduleSourceStore.PathFor(t.Id)).SequenceEqual(before), "decline replaced source");
        vm.DiscardUnfinishedSource();
        Check(!File.Exists(pending), "cancel left pending file");
    }
    static async Task PartialReimportPreserves()
    {
        var t = NewTimeline(); var tr = new FileTimelineRepository(); var lr = new FileLessonRepository();
        await SaveSource(t, Workbook("valid.xls")); await tr.AddAsync(t);
        await ScheduleReimportService.ReimportAsync(lr, tr, t, Log);
        var ids = (await lr.GetByTimelineIdAsync(t.Id)).Select(l => l.Id).ToHashSet();
        Check(ids.Count == 3, "baseline not three");
        var metadata = File.ReadAllText(Path.Combine(FileSystem.AppDataDirectory, "timelines.json"));
        await SaveSource(t, Workbook("partial.xls", true));
        var result = await ScheduleReimportService.ReimportAsync(lr, tr, t, Log);
        var remaining = (await lr.GetByTimelineIdAsync(t.Id)).ToList();
        Check(result.Status == ReimportStatus.IncompleteParse && result.Skipped == 1, "partial parse accepted");
        Check(ids.SetEquals(remaining.Select(l => l.Id)), "partial reimport changed lessons");
        Check(File.ReadAllText(Path.Combine(FileSystem.AppDataDirectory, "timelines.json")) == metadata, "partial reimport changed metadata");
    }
    static async Task PartialPickedImportPreserves()
    {
        var t = NewTimeline(); var tr = new FileTimelineRepository(); var lr = new FileLessonRepository();
        await SaveSource(t, Workbook("old.xls")); await tr.AddAsync(t); await lr.AddAsync(Lesson(t.Id));
        var before = File.ReadAllBytes(ScheduleSourceStore.PathFor(t.Id));
        var pending = await Stage(t, true);
        await Import(GroupVm(t, lr, tr, true, pending), "Б03-402");
        Check((await lr.GetByTimelineIdAsync(t.Id)).Single().Name == "Same lesson", "partial import changed lessons");
        Check((await tr.GetByIdAsync(t.Id))!.Source!.StoredFileName == null, "partial import published source");
        Check(t.Source!.GroupName == "Б03-401", "partial import mutated editor");
        Check(File.ReadAllBytes(ScheduleSourceStore.PathFor(t.Id)).SequenceEqual(before), "partial import overwrote source");
        Check(CurrentPage.Messages.Any(m => m.Title == "Файл разобран не полностью"), "missing parse diagnostic");
    }
    static async Task RetryUpdatesSource()
    {
        var t = new Timeline { Name = "Retry" }; var tr = new FileTimelineRepository(); var lr = new FailingLessons();
        var pending = await Stage(t);
        var vm = GroupVm(t, lr, tr, false, pending);
        lr.Remaining = 1; await Import(vm, "Б03-401");
        Check((await lr.GetByTimelineIdAsync(t.Id)).Count() == 1, "first import did not fail after first write");
        lr.Remaining = -1; await Import(vm, "Б03-402");
        var saved = (await tr.GetByIdAsync(t.Id))!;
        Check(saved.Source!.GroupName == "Б03-402", "retry kept old group");
        Check(saved.Source.StoredFileName == Path.GetFileName(pending), "retry did not commit source");
        Check(saved.BaseDays.Single().Day == DayOfWeek.Wednesday, "retry did not update base days");
        Check((await lr.GetByTimelineIdAsync(t.Id)).All(l => l.Name.StartsWith("ГруппаБ")), "retry did not import B");
        await ScheduleReimportService.ReimportAsync(lr, tr, saved, Log);
        Check((await lr.GetByTimelineIdAsync(t.Id)).All(l => l.Name.StartsWith("ГруппаБ")), "reimport reverted to old group");
        vm.DiscardUnfinishedSource();
        Check(File.Exists(pending), "successful source was discarded");
    }
    static async Task EmptyRetryPreserves()
    {
        var t = new Timeline { Name = "Retry" }; var tr = new FileTimelineRepository(); var lr = new FailingLessons();
        var pending = await Stage(t);
        var vm = GroupVm(t, lr, tr, false, pending);
        lr.Remaining = 1; await Import(vm, "Б03-401");
        Check((await lr.GetByTimelineIdAsync(t.Id)).Count() == 1, "first import did not leave a lesson");
        lr.Remaining = -1; await Import(vm, "Б03-403");
        Check((await lr.GetByTimelineIdAsync(t.Id)).Count() == 1, "empty retry deleted lesson");
        Check(CurrentPage.Messages.Any(m => m.Title == "Пары не найдены"), "empty retry not explained");
        vm.DiscardUnfinishedSource();
        Check(!File.Exists(pending), "failed attempt source not removed");
        Check((await lr.GetByTimelineIdAsync(t.Id)).Count() == 1, "cancel deleted saved lessons");
        Check(await tr.GetByIdAsync(t.Id) != null, "cancel deleted timeline");
    }
    static Lesson Lesson(Guid timeline) => new() { TimelineId = timeline, Name = "Same lesson", Day = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(9), EndTime = TimeSpan.FromHours(10) };
    static async Task LockedReadStopsReplacement()
    {
        var t = Guid.NewGuid(); var repo = new FileLessonRepository(); var old = Lesson(t);
        await repo.AddAsync(old);
        var path = Path.Combine(FileSystem.AppDataDirectory, "Timelines", t.ToString(), "Lessons", old.Id + ".json");
        int mutations = 0;
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            await Throws<IncompleteLessonReadException>(() => LessonImportService.ReplaceAllAsync(repo, t, [Lesson(t)], () => mutations++));
        var count = (await repo.GetByTimelineIdAsync(t)).Count();
        Check(count == 1 && mutations == 0, "locked read led to writes or duplicates");
    }
    static async Task CorruptReadStopsReplacement()
    {
        var t = Guid.NewGuid(); var repo = new FileLessonRepository(); var valid = Lesson(t);
        await repo.AddAsync(valid);
        var path = Path.Combine(FileSystem.AppDataDirectory, "Timelines", t.ToString(), "Lessons", Guid.NewGuid() + ".json");
        foreach (var json in new[] { "not json", "null", "{}" })
        {
            await File.WriteAllTextAsync(path, json);
            await Throws<IncompleteLessonReadException>(() => LessonImportService.ReplaceAllAsync(repo, t, [Lesson(t)]));
            Check(File.ReadAllText(path) == json, "damaged record removed");
            Check((await repo.GetByTimelineIdAsync(t)).Any(l => l.Id == valid.Id), "view lost valid lesson");
            Check(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.json").Length == 2, "replacement wrote new files");
        }
    }
    static async Task FailedWriteNotifies()
    {
        var t = NewTimeline(); var tr = new FileTimelineRepository(); var lr = new FailingLessons();
        await SaveSource(t, Workbook("notify.xls")); await tr.AddAsync(t);
        await lr.AddAsync(Lesson(t.Id));
        var pending = await Stage(t);
        int events = 0; void Changed(DayOfWeek? day) => events++;
        AppEvents.DataChanged += Changed;
        try
        {
            lr.Remaining = 1; await Import(GroupVm(t, lr, tr, true, pending), "Б03-402");
            var count = (await lr.GetByTimelineIdAsync(t.Id)).Count();
            Check(count == 2 && events == 1, "partial write did not notify exactly once");
            Check(t.Source!.StoredFileName == null && t.Source.GroupName == "Б03-401", "failed write published metadata");
        }
        finally { AppEvents.DataChanged -= Changed; }
    }
    static async Task FailedReimportNotifies() => await ReimportFailureNotifies(false);
    static async Task FailedDeletionNotifies() => await ReimportFailureNotifies(true);
    static async Task ReimportFailureNotifies(bool failDelete)
    {
        var t = NewTimeline(); var tr = new FileTimelineRepository(); var lr = new FailingLessons();
        await SaveSource(t, Workbook("reimportfailure.xls")); await tr.AddAsync(t); await lr.AddAsync(Lesson(t.Id));
        lr.Remaining = failDelete ? -1 : 1; lr.FailDelete = failDelete;
        var vm = new EditTimelineViewModel(tr, lr, new TestSettings(), null!, null!, new Nav(), null, t);
        int events = 0; void Changed(DayOfWeek? day) => events++;
        AppEvents.DataChanged += Changed;
        try
        {
            await Throws<IOException>(() => Call(vm, "HandleReimportAsync"));
            Check(events == 1, "reimport failure did not invalidate data");
            Check((await lr.GetByTimelineIdAsync(t.Id)).Count() == (failDelete ? 4 : 2), "failure was not after writes");
        }
        finally { AppEvents.DataChanged -= Changed; }
    }
    static async Task FailedMetadataKeepsSource()
    {
        var t = NewTimeline(); var tr = new FailingTimelines(); var lr = new FileLessonRepository();
        await SaveSource(t, Workbook("old.xls")); await tr.AddAsync(t); await lr.AddAsync(Lesson(t.Id));
        var pending = await Stage(t);
        var vm = GroupVm(t, lr, tr, true, pending);
        int events = 0; void Changed(DayOfWeek? day) => events++;
        AppEvents.DataChanged += Changed;
        try
        {
            tr.FailUpdate = true; await Import(vm, "Б03-402");
            Check(events == 1, "metadata failure did not notify changed lessons");
            Check(t.Source!.GroupName == "Б03-401" && t.Source.StoredFileName == null, "failed metadata changed editor");
            var persisted = (await tr.GetByIdAsync(t.Id))!;
            Check(persisted.Source!.StoredFileName == null, "failed metadata published new source");
            Check((await lr.GetByTimelineIdAsync(t.Id)).Count() == 3, "metadata failure occurred before replacement");
            tr.FailUpdate = false; await Import(vm, "Б03-402");
            Check((await tr.GetByIdAsync(t.Id))!.Source!.GroupName == "Б03-402", "metadata retry failed");
            Check(events == 2, "metadata retry notification missing");
        }
        finally { AppEvents.DataChanged -= Changed; }
    }
    static async Task CommittedSourceSurvives()
    {
        var t = new Timeline { Name = "New" }; var tr = new FileTimelineRepository(); var lr = new FileLessonRepository();
        var pending = await Stage(t);
        await Import(GroupVm(t, lr, tr, false, pending), "Б03-402");
        var saved = (await new FileTimelineRepository().GetByIdAsync(t.Id))!;
        Check(saved.Source!.StoredFileName == Path.GetFileName(pending), "source reference not persisted");
        var next = await Stage(saved, true);
        GroupVm(saved, lr, tr, true, next).DiscardUnfinishedSource();
        Check(File.Exists(pending) && !File.Exists(next), "cancellation damaged committed source");
        var result = await ScheduleReimportService.ReimportAsync(lr, tr, saved, Log);
        Check(result.Status == ReimportStatus.Success && result.Stored == 3, "new source cannot be reimported after restart");
        Check((await lr.GetByTimelineIdAsync(t.Id)).All(l => l.Name.StartsWith("ГруппаБ")), "wrong source after restart");
    }
    static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
    static async Task CachedSourceRefreshes()
    {
        var t = NewTimeline(); var tr = new FileTimelineRepository(); var lr = new FileLessonRepository();
        await SaveSource(t, Workbook("old.xls")); await tr.AddAsync(t);
        var management = new TimelinesViewModel(tr, new TestSettings(), new Provider());
        var flyout = new FlyoutViewModel(tr, new TestActiveSchedule(), new TestSettings());
        await management.LoadTimelinesAsync(); await flyout.LoadTimelinesAsync();
        var cached = management.Timelines.Single(); var flyoutItem = flyout.Timelines.Single();
        var pending = await Stage(t);
        await Import(GroupVm(t, lr, tr, true, pending), "Б03-402");
        await management.LoadTimelinesAsync(); await flyout.RefreshIfNeededAsync();
        Check(ReferenceEquals(cached, management.Timelines.Single()), "management unnecessarily replaced item");
        Check(ReferenceEquals(flyoutItem, flyout.Timelines.Single()), "flyout unnecessarily replaced item");
        foreach (var entry in new[] { cached, flyoutItem.Timeline })
        {
            Check(entry.Source!.StoredFileName == Path.GetFileName(pending), "cached editor kept old source");
            Check(entry.Source.GroupName == "Б03-402", "cached editor kept old group");
            var result = await ScheduleReimportService.ReimportAsync(lr, tr, entry, Log);
            Check(result.Status == ReimportStatus.Success, "cached editor cannot reimport new source");
            Check((await lr.GetByTimelineIdAsync(t.Id)).All(l => l.Name.StartsWith("ГруппаБ")), "cached editor reverted lessons");
        }
    }
    static async Task UnreadableLessonIsExplained()
    {
        var t = NewTimeline(); var tr = new FileTimelineRepository(); var lr = new FileLessonRepository();
        await SaveSource(t, Workbook("old.xls")); await tr.AddAsync(t);
        var lesson = Lesson(t.Id); await lr.AddAsync(lesson);
        var path = Path.Combine(FileSystem.AppDataDirectory, "Timelines", t.Id.ToString(), "Lessons", lesson.Id + ".json");
        var pending = await Stage(t); var vm = GroupVm(t, lr, tr, true, pending);
        int events = 0; void Changed(DayOfWeek? day) => events++;
        AppEvents.DataChanged += Changed;
        try
        {
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                await Import(vm, "Б03-402");
            Check(events == 0, "read-only failure invalidated data");
            Check(CurrentPage.Messages.Any(m => m.Message.Contains(lesson.Id.ToString())), "error did not name unreadable file");
            Check((await lr.GetByTimelineIdAsync(t.Id)).Single().Id == lesson.Id, "blocked import changed lessons");
            await Import(vm, "Б03-402");
            Check((await lr.GetByTimelineIdAsync(t.Id)).Count() == 3, "retry left duplicates");
        }
        finally { AppEvents.DataChanged -= Changed; }
    }
    static async Task UnreadablePickedFilePreserves()
    {
        var t = NewTimeline(); var tr = new FileTimelineRepository(); var lr = new FileLessonRepository();
        await SaveSource(t, Workbook("old.xls")); await tr.AddAsync(t); await lr.AddAsync(Lesson(t.Id));
        var before = File.ReadAllBytes(ScheduleSourceStore.PathFor(t.Id));
        using var bad = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not an Excel workbook"));
        var pending = await ScheduleSourceStore.StageAsync(t.Id, bad);
        await GroupVm(t, lr, tr, true, pending).InitializeAsync();
        Check(!File.Exists(pending), "failed initialization leaked pending file");
        Check(File.ReadAllBytes(ScheduleSourceStore.PathFor(t.Id)).SequenceEqual(before), "failed initialization changed source");
        Check((await lr.GetByTimelineIdAsync(t.Id)).Count() == 1, "failed initialization removed lessons");
    }
    sealed class FailingLessons : ILessonRepository
    {
        readonly FileLessonRepository inner = new(); public int Remaining = -1; public bool FailDelete;
        public Task AddAsync(Lesson l) { if (Remaining == 0) throw new IOException("injected write failure"); if (Remaining > 0) Remaining--; return inner.AddAsync(l); }
        public Task<IEnumerable<Lesson>> GetAllAsync() => inner.GetAllAsync();
        public Task<IEnumerable<Lesson>> GetByTimelineIdAsync(Guid id) => inner.GetByTimelineIdAsync(id);
        public Task<IEnumerable<Lesson>> GetByTimelineIdForReplacementAsync(Guid id) => inner.GetByTimelineIdForReplacementAsync(id);
        public Task<Lesson?> GetByIdAsync(Guid id) => inner.GetByIdAsync(id);
        public Task UpdateAsync(Lesson l) => inner.UpdateAsync(l);
        public Task DeleteAsync(Guid id) => inner.DeleteAsync(id);
        public Task DeleteManyAsync(Guid id, IEnumerable<Guid> ids) => FailDelete ? throw new IOException("injected delete failure") : inner.DeleteManyAsync(id, ids);
    }
    sealed class FailingTimelines : ITimelineRepository
    {
        readonly FileTimelineRepository inner = new(); public bool FailUpdate;
        public Task<IEnumerable<Timeline>> GetAllAsync() => inner.GetAllAsync();
        public Task<Timeline?> GetByIdAsync(Guid id) => inner.GetByIdAsync(id);
        public Task AddAsync(Timeline t) => inner.AddAsync(t);
        public Task UpdateAsync(Timeline t) => FailUpdate ? throw new IOException("injected metadata failure") : inner.UpdateAsync(t);
        public Task DeleteAsync(Guid id) => inner.DeleteAsync(id);
        public Task<bool> TryRecoverCorruptedAsync() => inner.TryRecoverCorruptedAsync();
    }
    sealed class Nav : INavigationService
    {
        public Task PushModalAsync(Page page) => Task.CompletedTask;
        public Task PopModalAsync() => Task.CompletedTask;
        public Task GoToAsync(string route) => Task.CompletedTask;
    }
    sealed class Provider : IServiceProvider { public object? GetService(Type type) => null; }
    sealed class Picker(string path) : IFilePickerService
    {
        public Task<FileResult?> PickExcelFileAsync() => Task.FromResult<FileResult?>(new() { FullPath = path, FileName = Path.GetFileName(path) });
    }
}
