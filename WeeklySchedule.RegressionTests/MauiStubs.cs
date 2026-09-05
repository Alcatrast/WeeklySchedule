// Только граница платформы. Тесты ниже не проверяют UI/навигацию MAUI.
global using Microsoft.Maui.Storage;
using System.Windows.Input;

public static class FileSystem
{
    public static string AppDataDirectory { get; set; } = "";
}
public enum AppTheme { Unspecified, Light, Dark }
public class Application
{
    public static Application? Current { get; set; }
    public List<Window> Windows { get; } = [];
    public AppTheme RequestedTheme { get; set; }
    public event EventHandler? RequestedThemeChanged { add { } remove { } }
}
public static class MainThread
{
    public static void BeginInvokeOnMainThread(Action action) => action();
}
public class Window
{
    public Page? Page { get; set; }
}
public class Page
{
    public TaskCompletionSource Alert { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task DisplayAlertAsync(string title, string message, string cancel)
    {
        Alert.TrySetResult();
        return Task.CompletedTask;
    }
    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel) => Task.FromResult(true);
}
public class Command(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
public class Command<T>(Action<T?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute((T?)parameter);
}
public class Shell
{
    public static Shell? Current { get; set; }
    public bool FlyoutIsPresented { get; set; }
    public TestNavigation Navigation { get; } = new();
}
public class TestNavigation
{
    public Task PushModalAsync(object page) => throw new NotSupportedException("UI is not under test");
}
public static class ServiceExtensions
{
    public static T GetRequiredService<T>(this IServiceProvider provider) => (T)provider.GetService(typeof(T))!;
}
namespace WeeklySchedule.Views
{
    public class LessonDetailsPage
    {
        public static Guid? LastOpened { get; set; }
        public static Task OpenAsync(Guid id) { LastOpened = id; return Task.CompletedTask; }
    }
    public class EditLessonPage
    {
        public EditLessonPage(Models.Lesson lesson) { }
        public static bool IsOpen => false;
        public static Task OpenModalAsync(EditLessonPage page, bool wrapInNavigationPage = false) =>
            throw new NotSupportedException("UI is not under test");
    }
    public class EditTimelinePage
    {
        public void Initialize(Models.Timeline? timeline) => throw new NotSupportedException("UI is not under test");
    }
}
namespace WeeklySchedule.Services
{
    public static class ItemActions
    {
        public static Guid? LastLessonMenu { get; set; }
        public static Guid? LastTimelineMenu { get; set; }
        public static Task ShowLessonAsync(Models.Lesson lesson) { LastLessonMenu = lesson.Id; return Task.CompletedTask; }
        public static Task ShowTimelineAsync(Models.Timeline timeline) { LastTimelineMenu = timeline.Id; return Task.CompletedTask; }
        public static Task OpenTimelineAsync(Models.Timeline? timeline) => Task.CompletedTask;
        public static Task<bool> DeleteTimelineAsync(Models.Timeline timeline) => Task.FromResult(false);
    }
}
namespace Microsoft.Maui.Storage
{
    public class FileResult
    {
        public string FullPath { get; set; } = "";
    }
}
