using WeeklySchedule.Core;
using WeeklySchedule.Models;
using WeeklySchedule.ViewModels;
using WeeklySchedule.Views;

static class DayViewLifecycleRegression
{
    public static (string, Func<Task>)[] Tests =>
    [
        ("Day content exists before the first native load, including free and base days", FirstBinding),
        ("Data arriving before native load updates already bound days", DataBeforeLoad),
        ("Recycled day views replace content before reload and release old subscriptions", Recycle),
        ("A minute tick with nothing to change does not repaint a day", QuietTick)
    ];
    private static void Check(bool condition) { if (!condition) throw new Exception("Day view lifecycle assertion failed"); }
    private static readonly DateTime Now = new(2026, 9, 8, 8, 0, 0);
    private static Lesson Lesson(DayOfWeek day) => new()
    {
        Day = day, Name = day.ToString(), StartTime = TimeSpan.FromHours(9), EndTime = TimeSpan.FromHours(10)
    };
    private static DayViewModel Day(int offset, WeekLayout week)
    {
        var day = new DayViewModel(Now.AddDays(offset));
        day.UpdateLayout(Now, week);
        return day;
    }
    private static Task FirstBinding()
    {
        var week = WeekLayout.Build([Lesson(DayOfWeek.Wednesday)],
            [new BaseDay { Day = DayOfWeek.Thursday, AllDay = true }]);
        var busy = new DayView { BindingContext = Day(1, week) };
        var marker = new DayView { BindingContext = Day(2, week) };
        var free = new DayView { BindingContext = Day(4, week) };
        Check(!busy.IsLoaded && !marker.IsLoaded && !free.IsLoaded);
        var card = busy.TimelineGrid.Children.OfType<LessonCardView>().Single();
        Check(card.Lesson!.Day == DayOfWeek.Wednesday && card.HeightRequest > 0);
        Check(marker.TimelineGrid.Children.OfType<BaseDayCardView>().Single().Text == "Базовый день");
        Check(free.TimelineGrid.Children.OfType<Label>().Any(l => l.Text == "Свободный день"));
        busy.RaiseLoaded();
        Check(ReferenceEquals(card, busy.TimelineGrid.Children.OfType<LessonCardView>().Single()));
        foreach (var view in new[] { busy, marker, free }) view.RaiseUnloaded();
        return Task.CompletedTask;
    }
    private static Task DataBeforeLoad()
    {
        var day = new DayViewModel(Now.AddDays(1));
        var view = new DayView { BindingContext = day };
        day.UpdateLayout(Now, WeekLayout.Build([Lesson(day.DayOfWeek)], []));
        Check(!view.IsLoaded && view.TimelineGrid.Children.OfType<LessonCardView>().Count() == 1);
        Check(!view.TimelineGrid.Children.OfType<Label>().Any(l => l.Text == "Свободный день"));
        view.RaiseLoaded();
        var card = view.TimelineGrid.Children.OfType<LessonCardView>().Single();
        day.UpdateLayout(Now.AddMinutes(1), WeekLayout.Build([Lesson(day.DayOfWeek)], []));
        // Новая раскладка переиспользует ту же карточку, а не строит вторую
        Check(ReferenceEquals(card, view.TimelineGrid.Children.OfType<LessonCardView>().Single()));
        view.RaiseUnloaded();
        return Task.CompletedTask;
    }
    // Планировщик будит экран раз в минуту. В чужом дне ни одна пара не текущая и
    // метки времени нет, поэтому обновлять там нечего, и раньше день все равно
    // перерисовывался целиком на каждое пробуждение
    private static Task QuietTick()
    {
        var week = WeekLayout.Build([Lesson(DayOfWeek.Wednesday)], []);
        var other = Day(1, week);
        var view = new DayView { BindingContext = other };
        view.RaiseLoaded();
        var card = view.TimelineGrid.Children.OfType<LessonCardView>().Single();
        int updates = card.Updates;
        other.UpdateLayout(Now.AddMinutes(1), week);
        other.UpdateLayout(Now.AddMinutes(2), week);
        Check(card.Updates == updates);
        // А принудительная перерисовка проходит: смену темы в раскладке не видно
        other.UpdateLayout(Now.AddMinutes(3), week, force: true);
        Check(card.Updates == updates + 1);
        view.RaiseUnloaded();
        return Task.CompletedTask;
    }

    // Раскладка пересобирается заново каждый раз намеренно: обновление состояния без
    // смены раскладки в чужом дне ничего не меняет и события больше не поднимает,
    // а проверяется здесь именно то, кому это событие достается
    private static WeekLayout Rebuilt() =>
        WeekLayout.Build([Lesson(DayOfWeek.Wednesday), Lesson(DayOfWeek.Friday)], []);

    private static Task Recycle()
    {
        var a = Day(1, Rebuilt()); var b = Day(3, Rebuilt());
        var view = new DayView();
        view.RaiseLoaded(); // The inverse order must work as well.
        view.BindingContext = a;
        var old = view.TimelineGrid.Children.OfType<LessonCardView>().Single();
        view.RaiseUnloaded();
        int updates = old.Updates;
        a.UpdateLayout(Now, Rebuilt());
        Check(old.Updates == updates);
        view.BindingContext = b;
        var replacement = view.TimelineGrid.Children.OfType<LessonCardView>().Single();
        // Карточка та же: смена дня переписывает ее содержимое, а не строит новую
        Check(replacement.Lesson!.Day == DayOfWeek.Friday && ReferenceEquals(old, replacement));
        updates = replacement.Updates;
        a.UpdateLayout(Now, Rebuilt());
        Check(replacement.Updates == updates);
        view.RaiseLoaded();
        updates = replacement.Updates;
        b.UpdateLayout(Now, Rebuilt());
        Check(replacement.Updates == updates + 1);
        view.RaiseUnloaded();
        b.UpdateLayout(Now, Rebuilt());
        Check(replacement.Updates == updates + 1);
        view.RaiseLoaded();
        Check(ReferenceEquals(replacement, view.TimelineGrid.Children.OfType<LessonCardView>().Single()));
        view.RaiseUnloaded();
        return Task.CompletedTask;
    }
}
