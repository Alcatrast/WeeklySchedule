using WeeklySchedule.Data;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Messaging;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;
using WeeklySchedule.Views;

static class InteractionRegression
{
    public static (string, Func<Task>)[] Tests =>
    [
        ("Returning without edits reuses data, layouts and scheduled notifications", Resume),
        ("Save and return share one data refresh", SaveAndReturn),
        ("Theme and duration settings do not rebuild notifications", UnrelatedSettings),
        ("Returning after midnight and a week keeps the day window current", ResumeAfterMidnight),
        ("Time-only update retains layout and lesson placements", StableLayout),
        ("Lesson edits rebuild geometry and preserve unchanged days", ChangedLayout),
        ("Lesson tap and menu dispatch distinct commands", Commands),
        ("Cancel and repeated deletion do not write twice", DeleteConfirmation),
        ("Timeline confirmation names the schedule and all its lessons", DeleteTimeline),
        ("Deleting the active last timeline recovers a usable default", DeleteLastTimeline),
        ("Lesson details refresh after edit, move and deletion", Details),
        ("Late lesson details cannot replace a newer response", LateDetails),
        ("Flyout return and selection retain items without rereading catalogue", FlyoutCache),
        ("Hold cancels on movement, release, rebind and unload", HoldGestures),
        ("Cold seed invalidates an already loaded empty flyout", SeededFlyout),
        ("Normal first launch creates one empty timeline without demo lessons", EmptyFirstLaunch),
        ("Unchanged settings preserve collection items and emit no UI changes", StableSettings),
        ("Repeated and concurrent imports do not duplicate lessons", RepeatedImport),
        ("Import preserves overlapping variants and other timelines", ImportVariants),
        ("Base-day badge follows selection without adding lessons", BaseDayBadge),
        ("Base-day metadata survives storage and legacy catalogues", BaseDayStorage),
        ("Excel imports base-day blocks separately from lessons", BaseDayImport),
        ("Cached timeline editors retain updated base-day metadata", BaseDayCache)
    ];
    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }

    private static async Task BaseDayCache()
    {
        var repo = new FileTimelineRepository(); var timeline = new Timeline { Name = "Main" };
        await repo.AddAsync(timeline);
        var vm = new TimelinesViewModel(repo, new TestSettings(), new EmptyProvider());
        await vm.LoadTimelinesAsync();
        var item = vm.Timelines.Single(); var flyout = new TimelineFlyoutItem(item);
        timeline.BaseDays.Add(new BaseDay { Day = DayOfWeek.Thursday, AllDay = true });
        await repo.UpdateAsync(timeline);
        await vm.LoadTimelinesAsync();
        Check(ReferenceEquals(item, vm.Timelines.Single()) && item.BaseDays.Count == 1);
        var other = new TimelineFlyoutItem(new Timeline { Id = timeline.Id });
        other.Update((await repo.GetByIdAsync(timeline.Id))!);
        Check(other.Timeline.BaseDays.Count == 1);
    }
    private sealed class EmptyProvider : IServiceProvider { public object? GetService(Type type) => null; }

    private static async Task BaseDayBadge()
    {
        var f = new Fixture();
        try
        {
            var date = TimeContext.Now;
            f.Repo.Timelines[0].BaseDays.Add(new BaseDay { Day = date.DayOfWeek, AllDay = true });
            await f.Main.InitializeDataAsync();
            f.Main.SelectedDayVM = f.Main.Days.Single(d => d.DayOfWeek == date.DayOfWeek);
            Check(f.Main.HasBaseDay && f.Main.BaseDayText == "Базовый день");
            Check(f.Main.SelectedDayVM.Layout.Lessons.Count == 1);
            f.Main.SelectedDayVM = f.Main.Days.First(d => d.DayOfWeek != date.DayOfWeek);
            Check(!f.Main.HasBaseDay);
            f.Main.SelectedDayVM = f.Main.Days.Single(d => d.DayOfWeek == date.DayOfWeek);
            f.Repo.Timelines[0].BaseDays.Clear();
            await f.Main.ReloadActiveTimelineAsync();
            Check(!f.Main.HasBaseDay);
        }
        finally { f.Main.StopMonitor(); }
    }

    private static async Task BaseDayStorage()
    {
        var legacy = System.Text.Json.JsonSerializer.Deserialize<Timeline>("{\"Name\":\"Old\"}")!;
        Check(legacy.BaseDays.Count == 0);
        var repo = new FileTimelineRepository();
        var marker = new BaseDay { Day = DayOfWeek.Thursday, StartTime = new(13, 55, 0), EndTime = new(15, 20, 0) };
        var timeline = new Timeline { BaseDays = [marker] };
        await repo.AddAsync(timeline);
        var loaded = (await repo.GetByIdAsync(timeline.Id))!;
        Check(loaded.BaseDays.Single() == marker);
        Check(marker.DisplayText == "Базовый день · 13:55–15:20");
        loaded.Name = "Renamed"; await repo.UpdateAsync(loaded);
        Check((await repo.GetByIdAsync(timeline.Id))!.BaseDays.Single() == marker);
    }

    private static Task BaseDayImport()
    {
        using var workbook = new NPOI.HSSF.UserModel.HSSFWorkbook();
        var sheet = workbook.CreateSheet("Schedule");
        void Cell(int row, int column, string text) => (sheet.GetRow(row) ?? sheet.CreateRow(row)).CreateCell(column).SetCellValue(text);
        Cell(0, 0, "Дни"); Cell(0, 1, "Часы"); Cell(0, 2, "Б03-401"); Cell(0, 3, "Б02-401");
        Cell(1, 0, "Четверг"); Cell(1, 1, "900 - 1025"); Cell(2, 1, "1035 - 1200");
        Cell(1, 2, "БАЗОВЫЙ ДЕНЬ"); Cell(2, 3, "Базовый день для кафедр СУ");
        sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(1, 2, 0, 0));
        sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(1, 2, 2, 2));
        var path = Path.Combine(FileSystem.AppDataDirectory, "base-day.xls");
        using (var stream = File.Create(path)) workbook.Write(stream);
        var parser = new WeeklySchedule.Extensions.ExcelMIPTScheduleParser(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WeeklySchedule.Extensions.ExcelMIPTScheduleParser>.Instance);
        Check(parser.ParseGroupSchedule(path, "Б03-401", out var allDay).Count == 0);
        Check(allDay.Count == 1 && allDay[0].AllDay && allDay[0].Day == DayOfWeek.Thursday);
        Check(parser.ParseGroupSchedule(path, "Б02-401", out var partial).Count == 0);
        Check(partial.Count == 1 && !partial[0].AllDay && partial[0].StartTime == new TimeSpan(10, 35, 0));
        Check(partial[0].Text == "Базовый день для кафедр СУ");
        Check(parser.ParseGroupSchedule(path, "Б99-999", out var missing).Count == 0 && missing.Count == 0);
        return Task.CompletedTask;
    }

    private static Lesson Imported(string description = "Teacher", LessonType type = LessonType.Seminar) => new()
    {
        Name = "Physics", Description = description, Type = type, Day = DayOfWeek.Tuesday,
        StartTime = TimeSpan.FromHours(9), EndTime = new TimeSpan(10, 25, 0)
    };

    private static async Task RepeatedImport()
    {
        var repo = new FileLessonRepository(); var timeline = Guid.NewGuid();
        var first = Imported();
        Check(await LessonImportService.AddMissingAsync(repo, timeline, [first, Imported()]) == 1);
        var results = await Task.WhenAll(
            LessonImportService.AddMissingAsync(repo, timeline, [Imported(), Imported("Other")]),
            LessonImportService.AddMissingAsync(repo, timeline, [Imported(), Imported("Other")]));
        Check(results.Sum() == 1);
        var stored = (await repo.GetByTimelineIdAsync(timeline)).ToList();
        Check(stored.Count == 2 && stored.Any(l => l.Id == first.Id));
        var day = new DayViewModel(new DateTime(2026, 9, 8));
        day.UpdateLayout(new DateTime(2026, 9, 6), stored.Where(l => l.Description == "Teacher").ToList());
        Check(day.Layout.Lessons.Count == 1 && day.Layout.TotalColumns == 1);
    }

    private static async Task ImportVariants()
    {
        var repo = new FileLessonRepository(); var timeline = Guid.NewGuid(); var other = Guid.NewGuid();
        Check(await LessonImportService.AddMissingAsync(repo, other, [Imported()]) == 1);
        Check(await LessonImportService.AddMissingAsync(repo, timeline,
            [Imported(), Imported("Other"), Imported(type: LessonType.Practice)]) == 3);
        Check((await repo.GetByTimelineIdAsync(other)).Count() == 1);
        Check((await repo.GetByTimelineIdAsync(timeline)).Count() == 3);
    }

    private static async Task EmptyFirstLaunch()
    {
        var repo = new Repository(); var active = new TestActiveSchedule();
        Application.Current = new Application();
        var main = new MainViewModel(repo, repo, new EmptyDataSeeder(), active, new TestSettings(),
            new NotificationNavigationService(), new Notifications());
        try
        {
            await main.InitializeDataAsync();
            Check(repo.Timelines.Count == 1 && repo.Timelines[0].Name == "Мое расписание" && repo.Lessons.Count == 0);
            repo.Lessons.Add(new Lesson { TimelineId = active.ActiveTimelineId, Name = "User lesson" });
            await new EmptyDataSeeder().SeedAsync(repo, repo, active);
            Check(repo.Lessons.Single().Name == "User lesson" && repo.Timelines.Count == 1);
        }
        finally { main.StopMonitor(); }
    }

    private static async Task StableSettings()
    {
        var repo = new Repository(); var timeline = new Timeline { Name = "Main" }; repo.Timelines.Add(timeline);
        var settings = new TestSettings { StartupTimelineId = timeline.Id,
            NotifyBeforeList = [new NotificationReminder { MinutesBefore = 10, IsActive = true }] };
        var vm = new SettingsViewModel(settings, repo, new Notifications());
        await vm.RefreshAsync();
        var selected = vm.SelectedStartupTimeline; var reminder = vm.ReminderItems.Single();
        int changes = 0;
        vm.StartupTimelines.CollectionChanged += (_, _) => changes++;
        vm.ReminderItems.CollectionChanged += (_, _) => changes++;
        vm.PropertyChanged += (_, _) => changes++;
        await vm.RefreshAsync();
        Check(changes == 0 && ReferenceEquals(selected, vm.SelectedStartupTimeline) && ReferenceEquals(reminder, vm.ReminderItems.Single()));
        settings.OpenLastTimeline = false;
        await vm.RefreshAsync();
        Check(vm.IsStartupPickerVisible && changes == 2);
    }

    private static async Task SeededFlyout()
    {
        var repo = new Repository(); var active = new TestActiveSchedule(); var settings = new TestSettings();
        Application.Current = new Application();
        var flyout = new FlyoutViewModel(repo, active, settings);
        await flyout.RefreshIfNeededAsync(); Check(flyout.Timelines.Count == 0);
        var main = new MainViewModel(repo, repo, new ColdSeeder(), active, settings, new NotificationNavigationService(), new Notifications());
        try
        {
            await main.InitializeDataAsync(); await flyout.RefreshIfNeededAsync();
            Check(flyout.Timelines.Count == 1 && flyout.Timelines[0].Name == "Seeded");
        }
        finally { main.StopMonitor(); }
    }

    private static Task HoldGestures()
    {
        var gesture = new HoldGestureState();
        var token = gesture.Begin(0, 0);
        gesture.Move(20, 0, 8);
        Check(!gesture.TryHold(token) && gesture.Cancelled);
        token = gesture.Begin(0, 0);
        Check(!gesture.End() && !gesture.TryHold(token));
        token = gesture.Begin(0, 0);
        gesture.Cancel();
        Check(!gesture.TryHold(token));
        token = gesture.Begin(0, 0);
        gesture.Move(2, 2, 8);
        Check(gesture.TryHold(token) && !gesture.TryHold(token) && gesture.End() && gesture.Held);
        gesture.Begin(0, 0);
        Check(!gesture.Held && !gesture.Cancelled);
        return Task.CompletedTask;
    }

    private static async Task Resume()
    {
        var f = new Fixture();
        try
        {
            await f.Main.InitializeDataAsync();
            var reads = f.Repo.LessonReads;
            var catalogueReads = f.Repo.TimelineReads;
            var cancellations = f.Notifications.Cancellations;
            var layouts = f.Main.Days.Select(d => d.Layout).ToArray();
            f.Main.StopMonitor();
            await f.Main.InitializeDataAsync();
            await f.Main.InitializeDataAsync();
            Check(f.Repo.LessonReads == reads && f.Repo.TimelineReads == catalogueReads);
            Check(f.Notifications.Cancellations == cancellations);
            Check(f.Main.Days.Select(d => d.Layout).SequenceEqual(layouts));
        }
        finally { f.Main.StopMonitor(); }
    }

    private static async Task SaveAndReturn()
    {
        var f = new Fixture();
        try
        {
            await f.Main.InitializeDataAsync();
            int reads = f.Repo.LessonReads;
            int cancellations = f.Notifications.Cancellations;
            f.Repo.Lessons[0].Name = "Changed";
            var pending = new TaskCompletionSource<IEnumerable<Lesson>>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Repo.NextLessons = pending.Task;
            AppEvents.NotifyDataChanged();
            var returning = f.Main.InitializeDataAsync();
            pending.SetResult(f.Repo.Lessons.ToList());
            await returning;
            Check(f.Repo.LessonReads == reads + 2); // One snapshot for cards, one for alarms.
            Check(f.Notifications.Cancellations == cancellations + 1);
            Check(f.Main.Days.SelectMany(d => d.Layout.Lessons).Single().Lesson.Name == "Changed");
        }
        finally { f.Main.StopMonitor(); }
    }

    private static async Task UnrelatedSettings()
    {
        var f = new Fixture();
        try
        {
            await f.Main.InitializeDataAsync();
            int reads = f.Repo.LessonReads, cancellations = f.Notifications.Cancellations;
            f.Settings.Theme = AppTheme.Dark;
            f.Settings.DefaultLessonDuration = 90;
            f.Settings.RaiseChanged();
            await f.Main.InitializeDataAsync();
            Check(f.Repo.LessonReads == reads && f.Notifications.Cancellations == cancellations);
            f.Settings.NotifyAtStart = false;
            f.Settings.RaiseChanged();
            Check(f.Notifications.Cancellations == cancellations + 1);
            await f.Main.InitializeDataAsync();
            Check(f.Notifications.Cancellations == cancellations + 1);
        }
        finally { f.Main.StopMonitor(); }
    }

    private static async Task ResumeAfterMidnight()
    {
        TimeContext.Now = new DateTime(2026, 9, 7, 23, 55, 0);
        var f = new Fixture();
        try
        {
            await f.Main.InitializeDataAsync();
            int reads = f.Repo.LessonReads;
            f.Main.StopMonitor();
            TimeContext.Now = new DateTime(2026, 9, 8, 0, 5, 0);
            await f.Main.InitializeDataAsync();
            Check(f.Main.Days.Count == 7 && f.Main.Days[0].Date == TimeContext.Now.Date);
            f.Main.StopMonitor();
            TimeContext.Now = new DateTime(2026, 9, 17, 10, 0, 0);
            await f.Main.InitializeDataAsync();
            Check(f.Main.Days.Count == 7 && f.Main.SelectedDayVM!.Date == TimeContext.Now.Date);
            Check(f.Repo.LessonReads == reads);
        }
        finally { f.Main.StopMonitor(); TimeContext.Now = DateTime.Now; }
    }

    private static Task StableLayout()
    {
        var day = new DayViewModel(new DateTime(2026, 9, 7));
        var lesson = new Lesson { Day = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(10), EndTime = TimeSpan.FromHours(11) };
        var next = new Lesson { Day = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(12), EndTime = TimeSpan.FromHours(13) };
        day.UpdateLayout(day.Date.AddHours(9), [lesson, next]);
        var layout = day.Layout;
        var placement = layout.Lessons[0];
        day.UpdateLayout(day.Date.AddHours(10.5), [lesson, next]);
        Check(ReferenceEquals(layout, day.Layout) && ReferenceEquals(placement, day.Layout.Lessons[0]) && placement.IsCurrent);
        day.UpdateLayout(day.Date.AddHours(11.5), [lesson, next]);
        Check(!placement.IsCurrent && ReferenceEquals(layout, day.Layout) && layout.Breaks.Count == 1);
        day.UpdateLayout(day.Date.AddDays(1), [lesson, next]);
        Check(!placement.IsCurrent && layout.Breaks.Count == 0);
        day.RequestScroll();
        Check(day.ScrollRequested);
        day.AcknowledgeScroll();
        Check(!day.ScrollRequested);
        return Task.CompletedTask;
    }

    private static Task ChangedLayout()
    {
        var day = new DayViewModel(DateTime.Today);
        var other = new DayViewModel(DateTime.Today.AddDays(1));
        var lesson = new Lesson { Day = day.DayOfWeek, Name = "Before", StartTime = TimeSpan.FromHours(10), EndTime = TimeSpan.FromHours(11) };
        day.UpdateLayout(DateTime.Now, [lesson]);
        other.UpdateLayout(DateTime.Now, [lesson]);
        var layout = day.Layout;
        var otherLayout = other.Layout;
        lesson.Name = "After"; lesson.StartTime = TimeSpan.FromHours(9);
        day.UpdateLayout(DateTime.Now, [lesson]);
        other.UpdateLayout(DateTime.Now, [lesson]);
        Check(!ReferenceEquals(layout, day.Layout) && day.Layout.Lessons.Single().TotalMinutes == 120);
        Check(ReferenceEquals(otherLayout, other.Layout));
        return Task.CompletedTask;
    }

    private static Task Commands()
    {
        var day = new DayViewModel(DateTime.Today);
        var lesson = new Lesson();
        LessonDetailsPage.LastOpened = null;
        ItemActions.LastLessonMenu = null;
        day.ViewLessonCommand.Execute(lesson);
        Check(LessonDetailsPage.LastOpened == lesson.Id && ItemActions.LastLessonMenu == null);
        day.LessonActionsCommand.Execute(lesson);
        Check(ItemActions.LastLessonMenu == lesson.Id);
        return Task.CompletedTask;
    }

    private static async Task DeleteConfirmation()
    {
        var repo = new Repository();
        var lesson = new Lesson { Name = "My lesson" }; repo.Lessons.Add(lesson);
        var service = new ItemDeletionService(repo, repo, new TestSettings());
        Check(!await service.DeleteLessonAsync(lesson, (_, text) => { Check(text.Contains(lesson.Name)); return Task.FromResult(false); }));
        Check(repo.Lessons.Count == 1 && repo.Deletions == 0);
        var answer = new TaskCompletionSource<bool>();
        var first = service.DeleteLessonAsync(lesson, (_, _) => answer.Task);
        Check(!await service.DeleteLessonAsync(lesson, (_, _) => throw new Exception("Repeated confirmation")));
        answer.SetResult(true);
        Check(await first && repo.Deletions == 1 && repo.Lessons.Count == 0);
    }

    private static async Task DeleteTimeline()
    {
        var timelines = new FileTimelineRepository();
        var lessons = new FileLessonRepository();
        var timeline = new Timeline { Name = "My schedule" };
        await timelines.AddAsync(timeline);
        await lessons.AddAsync(new Lesson { TimelineId = timeline.Id });
        var settings = new TestSettings { StartupTimelineId = timeline.Id };
        var service = new ItemDeletionService(lessons, timelines, settings);
        Check(!await service.DeleteTimelineAsync(timeline, (_, _) => Task.FromResult(false)));
        Check(settings.StartupTimelineId == timeline.Id && (await lessons.GetByTimelineIdAsync(timeline.Id)).Count() == 1);
        Check(await service.DeleteTimelineAsync(timeline, (_, message) =>
        {
            Check(message.Contains(timeline.Name) && message.Contains("все его пары"));
            return Task.FromResult(true);
        }));
        Check(settings.StartupTimelineId == Guid.Empty && !(await timelines.GetAllAsync()).Any());
        Check(!(await lessons.GetByTimelineIdAsync(timeline.Id)).Any());
    }

    private static async Task DeleteLastTimeline()
    {
        var f = new Fixture();
        try
        {
            await f.Main.InitializeDataAsync();
            var service = new ItemDeletionService(f.Repo, f.Repo, f.Settings);
            await service.DeleteTimelineAsync(f.Repo.Timelines[0], (_, _) => Task.FromResult(true));
            await f.Main.InitializeDataAsync();
            Check(f.Repo.Timelines.Count == 1 && f.Main.ActiveTimelineId == f.Repo.Timelines[0].Id);
            Check(f.Main.CurrentTimelineName == "Мое расписание" && !f.Main.Days.SelectMany(d => d.Layout.Lessons).Any());
        }
        finally { f.Main.StopMonitor(); }
    }

    private static async Task Details()
    {
        var repo = new Repository();
        var a = new Timeline { Name = "A" }; var b = new Timeline { Name = "B" };
        repo.Timelines.AddRange([a, b]);
        var lesson = new Lesson { TimelineId = a.Id, Name = "Before", Description = new string('x', 1000) };
        repo.Lessons.Add(lesson);
        var vm = new LessonDetailsViewModel(lesson.Id, repo, repo);
        await vm.RefreshAsync(); Check(vm.TimelineName == "A" && vm.Lesson!.Description.Length == 1000);
        lesson.Name = "After"; lesson.TimelineId = b.Id;
        await vm.RefreshAsync(); Check(vm.TimelineName == "B" && vm.Lesson!.Name == "After");
        repo.Lessons.Clear();
        await vm.RefreshAsync(); Check(vm.IsDeleted && vm.Lesson == null);
    }

    private static async Task LateDetails()
    {
        var repo = new Repository();
        var lesson = new Lesson { Name = "New" }; repo.Lessons.Add(lesson);
        var pending = new TaskCompletionSource<Lesson?>(); repo.NextLesson = pending.Task;
        var vm = new LessonDetailsViewModel(lesson.Id, repo, repo);
        var old = vm.RefreshAsync();
        await vm.RefreshAsync();
        pending.SetResult(new Lesson { Id = lesson.Id, Name = "Old" });
        await old; Check(vm.Lesson!.Name == "New");
        pending = new TaskCompletionSource<Lesson?>(); repo.NextLesson = pending.Task;
        old = vm.RefreshAsync(); vm.CancelPendingRefresh(); pending.SetResult(null);
        await old; Check(!vm.IsDeleted);
    }

    private static async Task FlyoutCache()
    {
        var repo = new Repository(); var a = new Timeline { Name = "A" }; var b = new Timeline { Name = "B" };
        repo.Timelines.AddRange([a, b]);
        var active = new TestActiveSchedule { ActiveTimelineId = a.Id }; var settings = new TestSettings();
        var vm = new FlyoutViewModel(repo, active, settings);
        await vm.RefreshIfNeededAsync();
        int reads = repo.TimelineReads; var first = vm.Timelines[0];
        active.ActiveTimelineId = b.Id; settings.Theme = AppTheme.Dark; settings.RaiseChanged();
        await vm.RefreshIfNeededAsync();
        Check(repo.TimelineReads == reads && ReferenceEquals(first, vm.Timelines[0]) && vm.Timelines[1].IsActive);
        a.Name = "Renamed"; AppEvents.NotifyDataChanged(); await vm.RefreshIfNeededAsync();
        Check(ReferenceEquals(first, vm.Timelines[0]) && first.Name == "Renamed");
    }

    private sealed class Fixture
    {
        public Repository Repo { get; } = new();
        public TestSettings Settings { get; } = new() { NotifyAtStart = true };
        public Notifications Notifications { get; } = new();
        public MainViewModel Main { get; }
        public Fixture()
        {
            Application.Current = new Application();
            var timeline = new Timeline { Name = "Main" }; Repo.Timelines.Add(timeline);
            Repo.Lessons.Add(new Lesson { TimelineId = timeline.Id, Day = TimeContext.Now.DayOfWeek, StartTime = TimeSpan.FromHours(10), EndTime = TimeSpan.FromHours(11) });
            Main = new MainViewModel(Repo, Repo, new Seeder(), new TestActiveSchedule { ActiveTimelineId = timeline.Id }, Settings,
                new NotificationNavigationService(), Notifications);
        }
    }
    private sealed class Seeder : IDataSeeder
    {
        public Task SeedAsync(ILessonRepository lessons, ITimelineRepository timelines, IActiveScheduleService active) => Task.CompletedTask;
    }
    private sealed class ColdSeeder : IDataSeeder
    {
        public async Task SeedAsync(ILessonRepository lessons, ITimelineRepository timelines, IActiveScheduleService active)
        {
            var timeline = new Timeline { Name = "Seeded" };
            await timelines.AddAsync(timeline); active.ActiveTimelineId = timeline.Id;
        }
    }
    private sealed class Repository : ILessonRepository, ITimelineRepository
    {
        public List<Lesson> Lessons { get; } = [];
        public List<Timeline> Timelines { get; } = [];
        public int LessonReads, TimelineReads, Deletions;
        public Task<IEnumerable<Lesson>>? NextLessons;
        public Task<Lesson?>? NextLesson;
        Task<IEnumerable<Lesson>> ILessonRepository.GetAllAsync() => Task.FromResult<IEnumerable<Lesson>>(Lessons.ToList());
        public Task<IEnumerable<Lesson>> GetByTimelineIdAsync(Guid id)
        {
            LessonReads++;
            if (NextLessons != null) { var task = NextLessons; NextLessons = null; return task; }
            return Task.FromResult<IEnumerable<Lesson>>(Lessons.Where(l => l.TimelineId == id).ToList());
        }
        Task<Lesson?> ILessonRepository.GetByIdAsync(Guid id)
        {
            if (NextLesson != null) { var task = NextLesson; NextLesson = null; return task; }
            return Task.FromResult(Lessons.FirstOrDefault(l => l.Id == id));
        }
        public Task AddAsync(Lesson lesson) { Lessons.Add(lesson); return Task.CompletedTask; }
        public Task UpdateAsync(Lesson lesson) => Task.CompletedTask;
        Task ILessonRepository.DeleteAsync(Guid id) { Deletions++; Lessons.RemoveAll(l => l.Id == id); return Task.CompletedTask; }
        public Task<IEnumerable<Timeline>> GetAllAsync() { TimelineReads++; return Task.FromResult<IEnumerable<Timeline>>(Timelines.ToList()); }
        Task<Timeline?> ITimelineRepository.GetByIdAsync(Guid id) { TimelineReads++; return Task.FromResult(Timelines.FirstOrDefault(t => t.Id == id)); }
        public Task AddAsync(Timeline timeline) { Timelines.Add(timeline); return Task.CompletedTask; }
        public Task UpdateAsync(Timeline timeline) => Task.CompletedTask;
        Task ITimelineRepository.DeleteAsync(Guid id) { Timelines.RemoveAll(t => t.Id == id); Lessons.RemoveAll(l => l.TimelineId == id); return Task.CompletedTask; }
    }
    private sealed class Notifications : INotificationService
    {
        public int Cancellations;
        public void CancelAllNotifications() => Cancellations++;
        public void ScheduleNotification(Guid timelineId, Guid lessonId, string title, string body, DateTime time, int minutes) { }
        public void CancelNotificationsForLesson(Guid id) { }
        public Task<bool> CheckPermissionAsync() => Task.FromResult(true);
        public Task<bool> CheckAllPermissionsAsync() => Task.FromResult(true);
        public Task RequestPermissionAsync() => Task.CompletedTask;
        public Task RequestAllPermissionsAsync() => Task.CompletedTask;
    }
}
