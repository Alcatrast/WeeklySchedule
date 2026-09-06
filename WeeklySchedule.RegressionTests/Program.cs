using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Lesson end follows moved start; midnight boundary is rejected", () =>
    {
        var start = TimeSpan.FromHours(12);
        var end = LessonTimeRange.NormalizeEnd(start, TimeSpan.FromHours(11));
        Check(end == new TimeSpan(12, 1, 0) && LessonTimeRange.IsValid(start, end));
        Check(!LessonTimeRange.IsValid(start, TimeSpan.FromHours(11)));
        Check(!LessonTimeRange.IsValid(LessonTimeRange.LatestEnd, LessonTimeRange.LatestEnd));
        Check(LessonTimeRange.IsValid(TimeSpan.Zero, TimeSpan.FromMinutes(1)));
        return Task.CompletedTask;
    }),
    ("Weekly reminder retains local time across DST", () =>
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var before = new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        var next = WeeklyOccurrence.Next(DayOfWeek.Sunday, TimeSpan.FromHours(10), 0, before, zone);
        Check(next == new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
        Check(TimeZoneInfo.ConvertTime(next, zone).Hour == 10);
        return Task.CompletedTask;
    }),
    ("Timezone change and long absence recalculate next local lesson", () =>
    {
        var after = new DateTimeOffset(2026, 9, 7, 5, 0, 0, TimeSpan.Zero);
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+5-test", TimeSpan.FromHours(5), "test", "test");
        var next = WeeklyOccurrence.Next(DayOfWeek.Monday, TimeSpan.FromHours(10), 0, after, zone);
        Check(next == after.AddDays(7));
        return Task.CompletedTask;
    }),
    ("Reminder before midnight and already elapsed reminder", () =>
    {
        var after = new DateTimeOffset(2026, 9, 6, 23, 0, 0, TimeSpan.Zero);
        var next = WeeklyOccurrence.Next(DayOfWeek.Monday, TimeSpan.FromMinutes(5), 10, after, TimeZoneInfo.Utc);
        Check(next == after.AddMinutes(55));
        Check(WeeklyOccurrence.Next(DayOfWeek.Monday, TimeSpan.FromMinutes(5), 10,
            after.AddMinutes(56), TimeZoneInfo.Utc) == next.AddDays(7));
        return Task.CompletedTask;
    }),
    ("Nonexistent and ambiguous DST times are deterministic", () =>
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var next = WeeklyOccurrence.Next(DayOfWeek.Sunday, new TimeSpan(2, 30, 0), 0,
            new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero), zone);
        Check(TimeZoneInfo.ConvertTime(next, zone).TimeOfDay == TimeSpan.FromHours(3));
        next = WeeklyOccurrence.Next(DayOfWeek.Sunday, new TimeSpan(2, 30, 0), 0,
            new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero), zone);
        Check(next == new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero));
        return Task.CompletedTask;
    }),
    ("Corrupt catalogue is backed up before recovery; reads do not fake empty data", async () =>
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, "timelines.json");
        File.WriteAllText(path, "broken-json");
        var repo = new FileTimelineRepository();
        await Throws<System.Text.Json.JsonException>(() => repo.GetAllAsync());
        var item = new Timeline { Name = "Recovered" };
        await repo.AddAsync(item);
        var backups = Directory.GetFiles(FileSystem.AppDataDirectory, "timelines.corrupted-*.json");
        Check(backups.Length == 1 && File.ReadAllText(backups[0]) == "broken-json");
        Check((await repo.GetAllAsync()).Single().Id == item.Id);
        await repo.AddAsync(item);
        Check((await repo.GetAllAsync()).Count() == 1);
    }),
    ("Read failures do not reset catalogue or create backups", async () =>
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, "timelines.json");
        File.WriteAllText(path, "[]");
        using (var locked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Throws<IOException>(() => new FileTimelineRepository().AddAsync(new Timeline()));
        Check(File.ReadAllText(path) == "[]");
        Check(Directory.GetFiles(FileSystem.AppDataDirectory, "timelines.corrupted-*.json").Length == 0);
    }),
    ("Failed atomic replacement preserves original and cleans temporary file", async () =>
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, "protected.json");
        File.WriteAllText(path, "original");
        using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await ThrowsFileAccess(() => AtomicFile.WriteAllText(path, "replacement"));
        Check(File.ReadAllText(path) == "original");
        Check(Directory.GetFiles(FileSystem.AppDataDirectory, "*.tmp").Length == 0);
    }),
    ("Lesson update and move preserve one complete copy", async () =>
    {
        var repo = new FileLessonRepository();
        var lesson = new Lesson { TimelineId = Guid.NewGuid(), Name = "Before", StartTime = TimeSpan.FromHours(10), EndTime = TimeSpan.FromHours(11) };
        var oldTimeline = lesson.TimelineId;
        await repo.AddAsync(lesson);
        lesson.Name = "After";
        lesson.TimelineId = Guid.NewGuid();
        await repo.UpdateAsync(lesson);
        Check(!(await repo.GetByTimelineIdAsync(oldTimeline)).Any());
        Check((await repo.GetAllAsync()).Single().Name == "After");
    }),
    ("Overlapping management loads publish only newest result", async () =>
    {
        var repo = new DelayedRepository();
        var vm = new TimelinesViewModel(repo, new TestSettings(), new TestServices());
        Check(repo.Pending.Count == 0); // Конструктор больше не начинает второй проход.
        var first = vm.LoadTimelinesAsync();
        var second = vm.LoadTimelinesAsync();
        repo.Pending[1].SetResult([new Timeline { Name = "new" }]);
        await second;
        repo.Pending[0].SetResult([new Timeline { Name = "old" }]);
        await first;
        Check(vm.Timelines.Single().Name == "new");
    }),
    ("Editor catches write failure and prevents duplicate pending save", async () =>
    {
        var repo = new PendingSaveRepository();
        var page = new Page();
        Application.Current = new Application();
        Application.Current.Windows.Add(new Window { Page = page });
        var vm = new EditTimelineViewModel(repo, new TestSettings(), null!, null!, null!, null) { Name = "New" };
        vm.SaveCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Check(repo.Writes == 1);
        repo.Pending.SetException(new IOException("Simulated disk failure"));
        await page.Alert.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Application.Current = null;
    }),
    ("Import completion can persist startup selection without saving the editor", () =>
    {
        var settings = new TestSettings();
        var timeline = new Timeline();
        var vm = new EditTimelineViewModel(new DelayedRepository(), settings, null!, null!, null!, timeline)
            { IsStartupTimeline = true };
        vm.ApplyStartupSelection();
        Check(settings.StartupTimelineId == timeline.Id && !settings.OpenLastTimeline);
        vm.IsStartupTimeline = false;
        vm.ApplyStartupSelection();
        Check(settings.StartupTimelineId == Guid.Empty);
        return Task.CompletedTask;
    }),
    ("Overlapping flyout loads publish only newest result", async () =>
    {
        var repo = new DelayedRepository();
        var vm = new FlyoutViewModel(repo, new TestActiveSchedule(), new TestSettings());
        var first = vm.LoadTimelinesAsync();
        var second = vm.LoadTimelinesAsync();
        repo.Pending[1].SetResult([new Timeline { Name = "new" }]);
        await second;
        repo.Pending[0].SetResult([new Timeline { Name = "old" }]);
        await first;
        Check(vm.Timelines.Single().Name == "new");
    })
};

tests = [.. tests, .. NavigationRegression.Tests, .. InteractionRegression.Tests];
var root = Directory.CreateTempSubdirectory("WeeklySchedule-regression-").FullName;
var failed = 0;
foreach (var (name, run) in tests)
{
    // Singleton-подписчики приложения не должны переходить из одного тестового
    // экземпляра приложения в другой. В реальном процессе они живут один раз.
    typeof(WeeklySchedule.Messaging.AppEvents).GetField("DataChanged",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, null);
    TimeContext.Now = DateTime.Now;
    FileSystem.AppDataDirectory = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N"))).FullName;
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed. Isolated data: {root}");
return failed == 0 ? 0 : 1;

static void Check(bool condition)
{
    if (!condition) throw new Exception("Assertion failed");
}
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
static Task ThrowsFileAccess(Action action)
{
    try { action(); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Task.CompletedTask; }
    throw new Exception("Expected file access failure");
}
sealed class DelayedRepository : ITimelineRepository
{
    public List<TaskCompletionSource<IEnumerable<Timeline>>> Pending { get; } = [];
    public Task<IEnumerable<Timeline>> GetAllAsync()
    {
        var result = new TaskCompletionSource<IEnumerable<Timeline>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending.Add(result);
        return result.Task;
    }
    public Task<Timeline?> GetByIdAsync(Guid id) => throw new NotSupportedException();
    public Task AddAsync(Timeline timeline) => throw new NotSupportedException();
    public Task UpdateAsync(Timeline timeline) => throw new NotSupportedException();
    public Task DeleteAsync(Guid id) => throw new NotSupportedException();
    public Task<bool> TryRecoverCorruptedAsync() => Task.FromResult(false);
}
sealed class TestSettings : ISettingsService
{
    public AppTheme Theme { get; set; }
    public int DefaultLessonDuration { get; set; } = 85;
    public bool OpenLastTimeline { get; set; } = true;
    public Guid StartupTimelineId { get; set; }
    public bool NotifyAtStart { get; set; }
    public List<NotificationReminder> NotifyBeforeList { get; set; } = [];
    public event Action? SettingsChanged;
    public void RaiseChanged() => SettingsChanged?.Invoke();
}
sealed class PendingSaveRepository : ITimelineRepository
{
    public int Writes { get; private set; }
    public TaskCompletionSource Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task AddAsync(Timeline timeline) { Writes++; return Pending.Task; }
    public Task<IEnumerable<Timeline>> GetAllAsync() => throw new NotSupportedException();
    public Task<Timeline?> GetByIdAsync(Guid id) => throw new NotSupportedException();
    public Task UpdateAsync(Timeline timeline) => throw new NotSupportedException();
    public Task DeleteAsync(Guid id) => throw new NotSupportedException();
    public Task<bool> TryRecoverCorruptedAsync() => Task.FromResult(false);
}
sealed class TestActiveSchedule : IActiveScheduleService
{
    private Guid _id;
    public Guid ActiveTimelineId
    {
        get => _id;
        set
        {
            if (_id == value) return;
            _id = value;
            ActiveTimelineChanged?.Invoke(value);
        }
    }
    public event Action<Guid>? ActiveTimelineChanged;
}
sealed class TestServices : IServiceProvider
{
    public object? GetService(Type serviceType) => throw new NotSupportedException();
}
