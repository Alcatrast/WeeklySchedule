using System.Globalization;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public sealed class LessonDetailsPage : ContentPage
{
    private static bool _opening;
    private readonly LessonDetailsViewModel _viewModel;
    private readonly Label _name = new() { FontSize = 22, FontAttributes = FontAttributes.Bold };
    private readonly Label _time = new() { FontSize = 16, FontAttributes = FontAttributes.Bold };
    private readonly Label _day = new() { FontSize = 16 };
    private readonly Label _type = new() { FontSize = 14 };
    private readonly Label _timeline = new() { FontSize = 14 };
    private readonly Label _description = new() { FontSize = 16, LineBreakMode = LineBreakMode.WordWrap };
    private Models.Lesson? _lesson;
    private bool _busy;
    private bool _hasAppeared, _visible, _closing;

    private LessonDetailsPage(Guid id)
    {
        var services = Application.Current!.Handler!.MauiContext!.Services;
        _viewModel = new LessonDetailsViewModel(id, services.GetRequiredService<ILessonRepository>(),
            services.GetRequiredService<ITimelineRepository>());
        Title = "Просмотр пары";
        var edit = new Button { Text = "Редактировать" };
        edit.Clicked += (_, _) => SafeFireAndForget.Run(EditAsync);
        var back = new ToolbarItem { Text = "Назад" };
        back.Clicked += (_, _) => SafeFireAndForget.Run(CloseAsync);
        ToolbarItems.Add(back);
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20, Spacing = 16,
                Children = { _name, _time, _day, _type, _timeline, _description, edit }
            }
        };
    }

    public static async Task OpenAsync(Guid id)
    {
        var navigation = Shell.Current?.Navigation;
        if (_opening || navigation == null || EditLessonPage.IsOpen ||
            navigation.ModalStack.Any(p => p is NavigationPage n && n.RootPage is LessonDetailsPage)) return;
        _opening = true;
        try
        {
            var page = new LessonDetailsPage(id);
            if (await page.RefreshAsync()) await navigation.PushModalAsync(new NavigationPage(page));
        }
        finally { _opening = false; }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        if (!_hasAppeared) { _hasAppeared = true; return; } // Уже загружено перед первым показом.
        SafeFireAndForget.Run(async () =>
        {
            if (!await RefreshAsync() && _visible) await CloseAsync();
        });
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        _viewModel.CancelPendingRefresh();
        base.OnDisappearing();
    }

    private async Task CloseAsync()
    {
        if (_closing || !_visible) return;
        _closing = true;
        try { await Navigation.PopModalAsync(); }
        finally { _closing = false; }
    }

    private async Task<bool> RefreshAsync()
    {
        await _viewModel.RefreshAsync();
        var lesson = _viewModel.Lesson;
        if (_viewModel.IsDeleted || lesson == null) return false;
        _lesson = lesson;
        _name.Text = lesson.Name;
        _time.Text = $"{lesson.StartTime:hh\\:mm} — {lesson.EndTime:hh\\:mm}";
        _day.Text = CultureInfo.GetCultureInfo("ru-RU").DateTimeFormat.GetDayName(lesson.Day);
        _type.Text = lesson.Type switch
        {
            Models.LessonType.Lecture => "Лекция", Models.LessonType.Seminar => "Семинар",
            Models.LessonType.Practice => "Практика", Models.LessonType.Lab => "Лабораторная", _ => "Занятие"
        };
        _timeline.Text = _viewModel.TimelineName;
        _description.Text = lesson.Description;
        _description.IsVisible = !string.IsNullOrWhiteSpace(lesson.Description);
        return true;
    }

    private async Task EditAsync()
    {
        if (_busy || _lesson == null) return;
        _busy = true;
        try { await EditLessonPage.OpenModalAsync(new EditLessonPage(_lesson), true); }
        finally { _busy = false; }
    }
}
