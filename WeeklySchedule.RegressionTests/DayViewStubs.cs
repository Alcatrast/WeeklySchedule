// Managed visual-tree boundary for the real DayView code-behind. These stubs
// let tests control binding/load order; they do not emulate Android measurement.
using WeeklySchedule.Models;
using WeeklySchedule.ViewModels;

public enum LayoutOptions { Start, Center, Fill }
public enum FontAttributes { None, Italic }
public readonly record struct Point(double X, double Y);
public readonly record struct Thickness(double Left, double Top, double Right, double Bottom);
public class Color { public static Color FromArgb(string value) => new(); }
public static class Colors { public static Color Gray { get; } = new(); }
public class SolidColorBrush(Color color) { public Color Color { get; } = color; }
public readonly record struct GridLength(double Value)
{
    public static GridLength Auto => new(-1);
    public static GridLength Star => new(-2);
}
public class RowDefinition(GridLength height = default) { public GridLength Height { get; set; } = height; }
public class ColumnDefinition(GridLength width) { public GridLength Width { get; } = width; }
public class View
{
    public LayoutOptions HorizontalOptions { get; set; }
    public LayoutOptions VerticalOptions { get; set; }
    public double WidthRequest { get; set; }
    public double HeightRequest { get; set; }
    public double MinimumHeightRequest { get; set; }
    public double Height { get; set; }
    public bool InputTransparent { get; set; }
    public bool IsVisible { get; set; } = true;
    public Thickness Margin { get; set; }
    public double TranslationY { get; set; }
    public string? StyleId { get; set; }
    internal int Row, RowSpan = 1, Column, ColumnSpan = 1;
    public event EventHandler? SizeChanged;
    public void RaiseSizeChanged() => SizeChanged?.Invoke(this, EventArgs.Empty);
}
public class ContentView : View
{
    private object? _context;
    public object? BindingContext
    {
        get => _context;
        set { if (ReferenceEquals(_context, value)) return; _context = value; BindingContextChanged?.Invoke(this, EventArgs.Empty); }
    }
    public bool IsLoaded { get; private set; }
    public TestDispatcher Dispatcher { get; } = new();
    public event EventHandler? BindingContextChanged;
    public event EventHandler? Loaded;
    public event EventHandler? Unloaded;
    public void RaiseLoaded() { IsLoaded = true; Loaded?.Invoke(this, EventArgs.Empty); }
    public void RaiseUnloaded() { IsLoaded = false; Unloaded?.Invoke(this, EventArgs.Empty); }
}
public class TestDispatcher
{
    private readonly Queue<Action> _pending = new();
    public void Dispatch(Action action) => _pending.Enqueue(action);
    public void Drain() { while (_pending.TryDequeue(out var action)) action(); }
}
public class Label : View
{
    public string Text { get; set; } = "";
    public double FontSize { get; set; }
    public FontAttributes FontAttributes { get; set; }
    public Color? TextColor { get; set; }
}
public class Grid : View
{
    public List<View> Children { get; } = [];
    public List<RowDefinition> RowDefinitions { get; } = [];
    public List<ColumnDefinition> ColumnDefinitions { get; } = [];
    public double ColumnSpacing { get; set; }
    public static void SetRow(View view, int value) => view.Row = value;
    public static void SetRowSpan(View view, int value) => view.RowSpan = value;
    public static void SetColumn(View view, int value) => view.Column = value;
    public static void SetColumnSpan(View view, int value) => view.ColumnSpan = value;
    public static int GetRow(View view) => view.Row;
    public static int GetRowSpan(View view) => view.RowSpan;
}
public class ScrollView : View
{
    public double ScrollY { get; private set; }
    public Task ScrollToAsync(double x, double y, bool animated) { ScrollY = y; return Task.CompletedTask; }
}
namespace Microsoft.Maui.Controls.Shapes
{
    public class Polygon : View
    {
        public List<Point> Points { get; set; } = [];
        public SolidColorBrush? Fill { get; set; }
    }
}
namespace WeeklySchedule.Views
{
    public partial class DayView
    {
        internal Grid TimelineGrid { get; } = new();
        internal ScrollView MainScroll { get; } = new();
        private void InitializeComponent() { }
    }
    public class LessonCardView : View
    {
        public Lesson? Lesson { get; private set; }
        public int Updates { get; private set; }
        public void Update(LessonPlacement placement, DayViewModel day, DateTime now)
        {
            Lesson = placement.Lesson;
            StyleId = placement.IsCurrent ? "CurrentLessonAnchor" : null;
            Updates++;
        }
    }
    public class BaseDayCardView : View
    {
        public string? Text { get; private set; }
        public void Update(MarkerPlacement placement) => Text = placement.Text;
    }
}
