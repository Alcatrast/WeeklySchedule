using WeeklySchedule.Core;
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
        ("Cold reload restores every weekday and leaves saved lesson files unchanged", ColdReloadWholeWeek),
        ("A day render failure does not leave the remaining days unloaded", FailedDayRender),
        ("Time-only update retains layout and lesson placements", StableLayout),
        ("Lesson edits rebuild the shared grid, unchanged data does not", ChangedLayout),
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
        ("A damaged lesson file does not empty the rest of the schedule", DamagedLessonFile),
        ("Base day is a block in the timeline, not a free day", BaseDayBlock),
        ("Base-day metadata survives storage and legacy catalogues", BaseDayStorage),
        ("Excel imports base-day blocks separately from lessons", BaseDayImport),
        ("Cached timeline editors retain updated base-day metadata", BaseDayCache)
    ];
    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }

    private static async Task FailedDayRender()
    {
        var f = new Fixture();
        try
        {
            foreach (var day in f.Main.Days.Skip(1))
                f.Repo.Lessons.Add(new Lesson
                {
                    TimelineId = f.Repo.Timelines[0].Id, Day = day.DayOfWeek,
                    StartTime = TimeSpan.FromHours(10), EndTime = TimeSpan.FromHours(11)
                });
            var failure = new InvalidOperationException("Injected day rendering failure");
            Action fail = () => throw failure;
            f.Main.Days[0].LayoutUpdated += fail;
            Exception? caught = null;
            try { await f.Main.InitializeDataAsync(); }
            catch (Exception ex) { caught = ex; }
            Check(caught != null);
            Check(f.Main.Days.All(d => d.Layout.Lessons.Count == 1));
            f.Main.Days[0].LayoutUpdated -= fail;
            int reads = f.Repo.LessonReads;
            await f.Main.InitializeDataAsync();
            Check(f.Repo.LessonReads > reads);
            Check(f.Main.Days.All(d => d.Layout.Lessons.Count == 1));
            Check(f.Notifications.Cancellations == 1);
        }
        finally { f.Main.StopMonitor(); }
    }

    private static async Task ColdReloadWholeWeek()
    {
        TimeContext.Now = new DateTime(2026, 9, 8, 12, 0, 0);
        var timelines = new FileTimelineRepository();
        var lessons = new FileLessonRepository();
        var timeline = new Timeline { Name = "Whole week" };
        await timelines.AddAsync(timeline);
        var expected = Enum.GetValues<DayOfWeek>().Select(day => new Lesson
        {
            TimelineId = timeline.Id, Day = day, Name = $"Lesson {day}",
            StartTime = TimeSpan.FromHours(9), EndTime = TimeSpan.FromHours(10)
        }).ToList();
        await LessonImportService.ReplaceAllAsync(lessons, timeline.Id, expected);
        var savedIds = (await lessons.GetByTimelineIdAsync(timeline.Id))
            .ToDictionary(l => l.Day, l => l.Id);
        var before = Directory.GetFiles(FileSystem.AppDataDirectory, "*.json", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText);

        // Новые репозитории и VM читают только файлы; нативная карусель здесь не проверяется.
        foreach (var elapsedDays in new[] { 0, 1, 8 })
        {
            TimeContext.Now = new DateTime(2026, 9, 8, 12, 0, 0).AddDays(elapsedDays);
            typeof(AppEvents).GetField("DataChanged",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, null);
            Application.Current = new Application();
            var main = new MainViewModel(new FileLessonRepository(), new FileTimelineRepository(),
                new EmptyDataSeeder(), new TestActiveSchedule { ActiveTimelineId = timeline.Id }, new TestSettings(),
                new NotificationNavigationService(), new Notifications());
            try
            {
                await main.InitializeDataAsync();
                Check(main.ActiveTimelineId == timeline.Id && main.Days.Count == 7);
                Check(main.Days.Select(d => d.DayOfWeek).Distinct().Count() == 7);
                foreach (var day in main.Days)
                {
                    main.SelectedDayVM = day;
                    Check(day.Layout.Lessons.Single().Lesson.Id == savedIds[day.DayOfWeek]);
                }
                main.StopMonitor();
                await main.InitializeDataAsync();
                Check(main.Days.All(d => d.Layout.Lessons.Count == 1));
                Check(before.All(file => File.ReadAllText(file.Key) == file.Value));
                Check(Directory.GetFiles(FileSystem.AppDataDirectory, "*.json", SearchOption.AllDirectories).Length == before.Count);
            }
            finally { main.StopMonitor(); }
        }
    }

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

    // Раньше пометка рисовалась плашкой над каруселью, а тело дня о ней не знало и
    // независимо писало «Свободный день»: базовый день выглядел свободным.
    private static async Task BaseDayBlock()
    {
        var f = new Fixture();
        try
        {
            // Пометка ставится в день без пар — именно он и показывал «Свободный день»
            var free = TimeContext.Now.AddDays(1).DayOfWeek;
            f.Repo.Timelines[0].BaseDays.Add(new BaseDay { Day = free, AllDay = true });
            await f.Main.InitializeDataAsync();

            var freeDay = f.Main.Days.Single(d => d.DayOfWeek == free);
            var marker = freeDay.Layout.Markers.Single();
            Check(marker.Text == "Базовый день");
            Check(freeDay.Layout.Lessons.Count == 0);   // пометка не стала парой
            // «На весь день» — значит на всю сетку
            Check(marker.StartRow == 0 && marker.RowSpan == freeDay.Layout.Segments.Count);

            var busy = f.Main.Days.Single(d => d.DayOfWeek == TimeContext.Now.DayOfWeek);
            Check(busy.Layout.Markers.Count == 0 && busy.Layout.Lessons.Count == 1);

            f.Repo.Timelines[0].BaseDays.Clear();
            await f.Main.ReloadActiveTimelineAsync();
            Check(f.Main.Days.All(d => d.Layout.Markers.Count == 0));
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
        // Соседние колонки группы описывают один слот: в расписание он ложится один раз
        var first = await LessonImportService.ReplaceAllAsync(repo, timeline, [Imported(), Imported()]);
        Check(first.Stored == 1 && first.Removed == 0);

        // Тот же файл во второй раз не копит, а заменяет
        var second = await LessonImportService.ReplaceAllAsync(repo, timeline, [Imported(), Imported("Other")]);
        Check(second.Stored == 2 && second.Removed == 1);

        // Два одновременных импорта тоже не оставляют четырех пар
        await Task.WhenAll(
            LessonImportService.ReplaceAllAsync(repo, timeline, [Imported(), Imported("Other")]),
            LessonImportService.ReplaceAllAsync(repo, timeline, [Imported(), Imported("Other")]));
        var stored = (await repo.GetByTimelineIdAsync(timeline)).ToList();
        Check(stored.Count == 2);

        var day = new DayViewModel(new DateTime(2026, 9, 8));
        day.UpdateLayout(new DateTime(2026, 9, 6),
            WeekLayout.Build([.. stored.Where(l => l.Description == "Teacher")], []));
        Check(day.Layout.Lessons.Count == 1 && day.Layout.TotalColumns == 1);
    }

    // Нечитаемый файл пары пропускался пустым catch: расписание оставалось в списке,
    // а неделя выходила пустой, и назвать потерю было нечем
    private static async Task DamagedLessonFile()
    {
        var repo = new FileLessonRepository(); var timeline = Guid.NewGuid();
        var kept = Imported();
        kept.TimelineId = timeline;
        await repo.AddAsync(kept);

        var lessonsDir = Path.Combine(FileSystem.AppDataDirectory, "Timelines", timeline.ToString(), "Lessons");
        File.WriteAllText(Path.Combine(lessonsDir, $"{Guid.NewGuid()}.json"), "broken-json");

        Check((await repo.GetByTimelineIdAsync(timeline)).Single().Id == kept.Id);
        Check((await repo.GetAllAsync()).Count(l => l.TimelineId == timeline) == 1);
    }

    private static async Task ImportVariants()
    {
        var repo = new FileLessonRepository(); var timeline = Guid.NewGuid(); var other = Guid.NewGuid();
        Check((await LessonImportService.ReplaceAllAsync(repo, other, [Imported()])).Stored == 1);
        Check((await LessonImportService.ReplaceAllAsync(repo, timeline,
            [Imported(), Imported("Other"), Imported(type: LessonType.Practice)])).Stored == 3);
        // Замена не выходит за свой таймлайн
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
        var week = WeekLayout.Build([lesson, next], []);
        day.UpdateLayout(day.Date.AddHours(9), week);
        var layout = day.Layout;
        var placement = layout.Lessons[0];
        day.UpdateLayout(day.Date.AddHours(10.5), week);
        Check(ReferenceEquals(layout, day.Layout) && ReferenceEquals(placement, day.Layout.Lessons[0]) && placement.IsCurrent);
        day.UpdateLayout(day.Date.AddHours(11.5), week);
        // Метка времени стоит в перерыве и не привязана к границам пар: раньше на ее
        // месте была полоска BreakPlacement по центру всего перерыва
        Check(!placement.IsCurrent && ReferenceEquals(layout, day.Layout));
        Check(layout.CurrentTimeOffset > TimelineMetrics.SpanHeight(layout.RowHeights, 0, 1));
        day.UpdateLayout(day.Date.AddDays(1), week);
        Check(!placement.IsCurrent && layout.CurrentTimeOffset == null);
        day.RequestScroll();
        Check(day.ScrollRequested);
        day.AcknowledgeScroll();
        Check(!day.ScrollRequested);
        return Task.CompletedTask;
    }

    // Сетка общая для недели, поэтому пересборкой заведует MainViewModel: лишняя
    // раскладка стоила бы DayView полной перерисовки, он сравнивает их по ссылке.
    private static async Task ChangedLayout()
    {
        var f = new Fixture();
        try
        {
            await f.Main.InitializeDataAsync();
            var day = f.Main.Days.Single(d => d.DayOfWeek == TimeContext.Now.DayOfWeek);
            var other = f.Main.Days.First(d => d.DayOfWeek != TimeContext.Now.DayOfWeek);
            var layout = day.Layout;
            var otherLayout = other.Layout;

            await f.Main.ReloadActiveTimelineAsync();
            Check(ReferenceEquals(layout, day.Layout) && ReferenceEquals(otherLayout, other.Layout));

            var lesson = f.Repo.Lessons[0];
            lesson.Name = "After"; lesson.StartTime = TimeSpan.FromHours(9);
            await f.Main.ReloadActiveTimelineAsync();
            Check(!ReferenceEquals(layout, day.Layout) && day.Layout.Lessons.Single().TotalMinutes == 120);
            // Новая граница времени меняет разметку всех дней, а не только своего
            Check(!ReferenceEquals(otherLayout, other.Layout));
        }
        finally { f.Main.StopMonitor(); }
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
        public Task DeleteManyAsync(Guid timelineId, IEnumerable<Guid> ids)
        {
            var set = ids.ToHashSet();
            Deletions += Lessons.RemoveAll(l => l.TimelineId == timelineId && set.Contains(l.Id));
            return Task.CompletedTask;
        }
        public Task<IEnumerable<Timeline>> GetAllAsync() { TimelineReads++; return Task.FromResult<IEnumerable<Timeline>>(Timelines.ToList()); }
        Task<Timeline?> ITimelineRepository.GetByIdAsync(Guid id) { TimelineReads++; return Task.FromResult(Timelines.FirstOrDefault(t => t.Id == id)); }
        public Task AddAsync(Timeline timeline) { Timelines.Add(timeline); return Task.CompletedTask; }
        public Task UpdateAsync(Timeline timeline) => Task.CompletedTask;
        Task ITimelineRepository.DeleteAsync(Guid id) { Timelines.RemoveAll(t => t.Id == id); Lessons.RemoveAll(l => l.TimelineId == id); return Task.CompletedTask; }
        public Task<bool> TryRecoverCorruptedAsync() => Task.FromResult(false);
    }
    private sealed class Notifications : INotificationService
    {
        public int Cancellations;
        public void CancelAllNotifications() => Cancellations++;
        public void ScheduleNotification(Guid timelineId, Guid lessonId, string title, string body,
            DayOfWeek day, TimeSpan startTime, int minutes) { }
        public Task<bool> CheckPermissionAsync() => Task.FromResult(true);
        public Task<bool> CanScheduleExactAlarmsAsync() => Task.FromResult(true);
        public Task RequestPermissionAsync() => Task.CompletedTask;
        public Task RequestExactAlarmsAsync() => Task.CompletedTask;
    }
}
