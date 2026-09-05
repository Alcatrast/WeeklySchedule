using WeeklySchedule.Models;
using WeeklySchedule.Views;

namespace WeeklySchedule.Services;

public static class ItemActions
{
    private static bool _busy;
    private static IServiceProvider Services => Application.Current!.Handler!.MauiContext!.Services;
    private static Page? CurrentPage => Shell.Current?.Navigation.ModalStack.LastOrDefault() is Page modal
        ? modal is NavigationPage navigation ? navigation.CurrentPage : modal
        : Shell.Current?.CurrentPage;

    private static Task<bool> ConfirmAsync(string title, string message) =>
        CurrentPage?.DisplayAlertAsync(title, message, "Удалить", "Отмена") ?? Task.FromResult(false);

    public static Task<bool> DeleteLessonAsync(Lesson lesson) =>
        Services.GetRequiredService<ItemDeletionService>().DeleteLessonAsync(lesson, ConfirmAsync);

    public static Task<bool> DeleteTimelineAsync(Timeline timeline) =>
        Services.GetRequiredService<ItemDeletionService>().DeleteTimelineAsync(timeline, ConfirmAsync);

    public static Task ShowLessonAsync(Lesson lesson) => RunAsync(async () =>
    {
        var page = CurrentPage;
        if (page == null || EditLessonPage.IsOpen) return;
        var action = await page.DisplayActionSheetAsync(lesson.Name, "Отмена", "Удалить", "Редактировать");
        if (action == "Редактировать")
            await EditLessonPage.OpenModalAsync(new EditLessonPage(lesson), true);
        else if (action == "Удалить") await DeleteLessonAsync(lesson);
    });

    public static Task ShowTimelineAsync(Timeline timeline) => RunAsync(async () =>
    {
        if (Shell.Current == null) return;
        Shell.Current.FlyoutIsPresented = false;
        var page = CurrentPage;
        if (page == null) return;
        var action = await page.DisplayActionSheetAsync(timeline.Name, "Отмена", "Удалить", "Редактировать");
        if (action == "Редактировать") await OpenTimelineCoreAsync(timeline);
        else if (action == "Удалить") await DeleteTimelineAsync(timeline);
    });

    public static Task OpenTimelineAsync(Timeline? timeline) => RunAsync(() => OpenTimelineCoreAsync(timeline));

    private static async Task OpenTimelineCoreAsync(Timeline? timeline)
    {
        var navigation = Shell.Current?.Navigation;
        if (navigation == null || navigation.ModalStack.Any(p => p is EditTimelinePage)) return;
        var page = Services.GetRequiredService<EditTimelinePage>();
        page.Initialize(timeline);
        await navigation.PushModalAsync(page);
    }

    private static async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try { await action(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (CurrentPage is Page page)
                await page.DisplayAlertAsync("Ошибка", "Не удалось завершить действие. Попробуйте ещё раз.", "ОК");
        }
        finally { _busy = false; }
    }
}
