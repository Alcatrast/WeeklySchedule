using WeeklySchedule.Data;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.ViewModels;

static class NavigationRegression
{
    public static (string, Func<Task>)[] Tests =>
    [
        ("Late timeline load cannot replace current cards or alarms", LateLoad),
        ("A-B-A selection rejects the first A response", RepeatedSelection),
        ("Late notification request cannot cancel newer alarms", LateAlarms),
        ("Cold notification launch wins over pinned startup timeline", ColdNavigation),
        ("Notification arriving during startup is not lost", NavigationDuringStartup),
        ("Warm notification navigation updates the already open main view", WarmNavigation),
        ("Settings refresh reflects added, renamed and deleted timelines", RefreshSettings),
        ("Stale settings refresh cannot replace the newest list", StaleSettings),
        ("Rebinding and unloading day view releases both event handlers", DaySubscriptions)
    ];

    private static void Check(bool condition)
    {
        if (!condition) throw new Exception("Assertion failed");
    }

    private static async Task LateLoad()
    {
        var f = new Fixture();
        var pending = f.Lessons.DelayNext(f.A.Id);
        var old = f.VM.ReloadActiveTimelineAsync();
        f.Active.ActiveTimelineId = f.B.Id;
        await f.VM.ReloadActiveTimelineAsync();
        pending.SetResult([f.LessonA]);
        await old;
        Check(f.VM.CurrentTimelineName == "B");
        Check(f.VM.Days.SelectMany(d => d.Layout.Lessons).Single().Lesson.Id == f.LessonB.Id);
        Check(f.Notifications.Scheduled.Single() == (f.B.Id, f.LessonB.Id));
        f.VM.StopMonitor();
    }

    private static async Task RepeatedSelection()
    {
        var f = new Fixture();
        var pending = f.Lessons.DelayNext(f.A.Id);
        var old = f.VM.ReloadActiveTimelineAsync();
        f.Active.ActiveTimelineId = f.B.Id;
        await f.VM.ReloadActiveTimelineAsync();
        f.Active.ActiveTimelineId = f.A.Id;
        var revised = new Lesson { TimelineId = f.A.Id, Name = "revised", StartTime = TimeSpan.FromHours(10), EndTime = TimeSpan.FromHours(11) };
        f.Lessons.Items[f.A.Id] = [revised];
        await f.VM.ReloadActiveTimelineAsync();
        pending.SetResult([f.LessonA]);
        await old;
        Check(f.VM.Days.SelectMany(d => d.Layout.Lessons).Single().Lesson.Id == revised.Id);
        f.VM.StopMonitor();
    }

    private static async Task LateAlarms()
    {
        var f = new Fixture();
        var pending = f.Lessons.DelayNext(f.A.Id);
        var old = f.VM.ScheduleAllNotificationsAsync();
        f.Active.ActiveTimelineId = f.B.Id;
        await f.VM.ScheduleAllNotificationsAsync();
        var cancellations = f.Notifications.Cancellations;
        pending.SetResult([f.LessonA]);
        await old;
        Check(f.Notifications.Cancellations == cancellations);
        Check(f.Notifications.Scheduled.Single() == (f.B.Id, f.LessonB.Id));
        f.VM.StopMonitor();
    }

    private static async Task ColdNavigation()
    {
        var f = new Fixture();
        f.Settings.OpenLastTimeline = false;
        f.Settings.StartupTimelineId = f.A.Id;
        f.Nav.SetPendingNavigation(f.B.Id);
        f.VM.CheckPendingNavigation();
        Check(f.Nav.PendingTimelineId == f.B.Id);
        await f.VM.InitializeDataAsync();
        Check(f.Active.ActiveTimelineId == f.B.Id && f.VM.CurrentTimelineName == "B");
        Check(f.Nav.PendingTimelineId == null);
        Check(f.Settings.StartupTimelineId == f.A.Id);
        f.VM.StopMonitor();
    }

    private static async Task NavigationDuringStartup()
    {
        var f = new Fixture();
        f.Settings.OpenLastTimeline = false;
        f.Settings.StartupTimelineId = f.A.Id;
        f.Seeder.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var startup = f.VM.InitializeDataAsync();
        f.Nav.SetPendingNavigation(f.B.Id);
        f.Seeder.Pending.SetResult();
        await startup;
        Check(f.Active.ActiveTimelineId == f.B.Id && f.VM.CurrentTimelineName == "B");
        f.VM.StopMonitor();
    }

    private static async Task RefreshSettings()
    {
        var f = new Fixture();
        var vm = new SettingsViewModel(f.Settings, f.Timelines, f.Notifications);
        await vm.RefreshAsync();
        var c = new Timeline { Name = "C" };
        f.Timelines.Items.Remove(f.A);
        f.B.Name = "B renamed";
        f.Timelines.Items.Add(c);
        f.Settings.StartupTimelineId = c.Id;
        f.Settings.OpenLastTimeline = false;
        await vm.RefreshAsync();
        Check(vm.StartupTimelines.Count == 2 && vm.StartupTimelines.All(t => t.Id != f.A.Id));
        Check(vm.StartupTimelines.Any(t => t.Name == "B renamed"));
        Check(vm.SelectedStartupTimeline?.Id == c.Id && !vm.OpenLast);
        f.Timelines.Items.Remove(c);
        await vm.RefreshAsync();
        Check(vm.SelectedStartupTimeline == null);
        Check(f.Settings.StartupTimelineId == c.Id); // Чтение не переписывает настройки.
    }

    private static async Task WarmNavigation()
    {
        var f = new Fixture();
        await f.VM.InitializeDataAsync();
        f.Nav.SetPendingNavigation(f.B.Id);
        Check(f.Active.ActiveTimelineId == f.B.Id && f.VM.CurrentTimelineName == "B");
        Check(f.Nav.PendingTimelineId == null);
        f.VM.StopMonitor();
    }

    private static async Task StaleSettings()
    {
        var repo = new DelayedRepository();
        var vm = new SettingsViewModel(new TestSettings(), repo, new RecordingNotifications());
        var old = vm.RefreshAsync();
        var current = vm.RefreshAsync();
        repo.Pending[1].SetResult([new Timeline { Name = "new" }]);
        await current;
        repo.Pending[0].SetResult([new Timeline { Name = "old" }]);
        await old;
        Check(vm.StartupTimelines.Single().Name == "new");
    }

    private static Task DaySubscriptions()
    {
        var a = new DayViewModel(DateTime.Today);
        var b = new DayViewModel(DateTime.Today.AddDays(1));
        var layouts = 0;
        var scrolls = 0;
        using var subscription = new DayViewSubscription(() => layouts++, () => scrolls++);
        subscription.SetSource(a);
        subscription.SetSource(b);
        subscription.SetSource(b);
        a.UpdateLayout(DateTime.Now, []);
        a.RequestScroll();
        Check(layouts == 0 && scrolls == 0);
        b.UpdateLayout(DateTime.Now, []);
        b.RequestScroll();
        Check(layouts == 1 && scrolls == 1);
        subscription.Dispose();
        b.UpdateLayout(DateTime.Now, []);
        b.RequestScroll();
        Check(layouts == 1 && scrolls == 1);
        subscription.SetSource(a); // Повторная загрузка того же View.
        a.RequestScroll();
        Check(scrolls == 2);
        return Task.CompletedTask;
    }

    private sealed class Fixture
    {
        public Timeline A { get; } = new() { Name = "A" };
        public Timeline B { get; } = new() { Name = "B" };
        public Lesson LessonA { get; }
        public Lesson LessonB { get; }
        public MutableTimelines Timelines { get; } = new();
        public ControlledLessons Lessons { get; } = new();
        public TestSettings Settings { get; } = new() { NotifyAtStart = true };
        public TestActiveSchedule Active { get; } = new();
        public NotificationNavigationService Nav { get; } = new();
        public RecordingNotifications Notifications { get; } = new();
        public TestSeeder Seeder { get; } = new();
        public MainViewModel VM { get; }
        public Fixture()
        {
            Application.Current = new Application();
            Timelines.Items.AddRange([A, B]);
            LessonA = new() { TimelineId = A.Id, StartTime = TimeSpan.FromHours(10), EndTime = TimeSpan.FromHours(11) };
            LessonB = new() { TimelineId = B.Id, StartTime = TimeSpan.FromHours(12), EndTime = TimeSpan.FromHours(13) };
            Lessons.Items[A.Id] = [LessonA];
            Lessons.Items[B.Id] = [LessonB];
            Active.ActiveTimelineId = A.Id;
            VM = new MainViewModel(Lessons, Timelines, Seeder, Active, Settings, Nav, Notifications);
        }
    }

    private sealed class MutableTimelines : ITimelineRepository
    {
        public List<Timeline> Items { get; } = [];
        public Task<IEnumerable<Timeline>> GetAllAsync() => Task.FromResult<IEnumerable<Timeline>>(Items.ToList());
        public Task<Timeline?> GetByIdAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(t => t.Id == id));
        public Task AddAsync(Timeline timeline) { Items.Add(timeline); return Task.CompletedTask; }
        public Task UpdateAsync(Timeline timeline) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class ControlledLessons : ILessonRepository
    {
        public Dictionary<Guid, List<Lesson>> Items { get; } = [];
        private readonly Dictionary<Guid, TaskCompletionSource<IEnumerable<Lesson>>> _pending = [];
        public TaskCompletionSource<IEnumerable<Lesson>> DelayNext(Guid id)
        {
            var pending = new TaskCompletionSource<IEnumerable<Lesson>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = pending;
            return pending;
        }
        public Task<IEnumerable<Lesson>> GetByTimelineIdAsync(Guid id) => _pending.Remove(id, out var pending)
            ? pending.Task : Task.FromResult<IEnumerable<Lesson>>(Items.GetValueOrDefault(id) ?? []);
        public Task<IEnumerable<Lesson>> GetAllAsync() => throw new NotSupportedException();
        public Task<Lesson?> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task AddAsync(Lesson lesson) => throw new NotSupportedException();
        public Task UpdateAsync(Lesson lesson) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class TestSeeder : IDataSeeder
    {
        public TaskCompletionSource? Pending { get; set; }
        public Task SeedAsync(ILessonRepository lessons, ITimelineRepository timelines, IActiveScheduleService active) => Pending?.Task ?? Task.CompletedTask;
    }

    private sealed class RecordingNotifications : INotificationService
    {
        public int Cancellations { get; private set; }
        public List<(Guid Timeline, Guid Lesson)> Scheduled { get; } = [];
        public void CancelAllNotifications() { Cancellations++; Scheduled.Clear(); }
        public void ScheduleNotification(Guid timelineId, Guid lessonId, string title, string body, DateTime time, int minutes) => Scheduled.Add((timelineId, lessonId));
        public Task<bool> CheckPermissionAsync() => Task.FromResult(true);
        public Task<bool> CheckAllPermissionsAsync() => Task.FromResult(true);
        public Task RequestPermissionAsync() => Task.CompletedTask;
        public Task RequestAllPermissionsAsync() => Task.CompletedTask;
        public void CancelNotificationsForLesson(Guid lessonId) => throw new NotSupportedException();
    }
}
