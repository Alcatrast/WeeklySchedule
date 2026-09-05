using System.Globalization;
using WeeklySchedule.Models;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public sealed class LessonCardView : Border
{
    private static readonly Converters.LessonTypeToColorConverter LessonColor = new();
    private readonly Label _time = new() { FontSize = 11, FontAttributes = FontAttributes.Bold, Opacity = 0.8, LineBreakMode = LineBreakMode.NoWrap };
    private readonly Label _name = new() { FontSize = 14, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly Label _description = new() { FontSize = 11, Opacity = 0.7, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly Button _menu;
    private readonly VerticalStackLayout _text;

    public LessonCardView()
    {
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 };
        Padding = new Thickness(10);
        Margin = new Thickness(3, 2);
        _text = new VerticalStackLayout
        {
            Spacing = 4, VerticalOptions = LayoutOptions.Fill, Margin = new Thickness(0, 2, 0, 0),
            Children = { _time, _name, _description }, BackgroundColor = Colors.Transparent
        };
        _time.InputTransparent = _name.InputTransparent = _description.InputTransparent = true;
        _menu = new Button
        {
            Text = "⋮", FontSize = 22, Padding = 0, WidthRequest = 40, HeightRequest = 44,
            MinimumHeightRequest = 44, MinimumWidthRequest = 40,
            BackgroundColor = Colors.Transparent, VerticalOptions = LayoutOptions.Start
        };
        _menu.SetAppThemeColor(Button.TextColorProperty, Colors.Black, Colors.White);
        SemanticProperties.SetDescription(_menu, "Действия с парой");
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(_text);
        grid.Add(_menu, 1);
        Content = grid;
    }

    public void Update(LessonPlacement placement, DayViewModel day, DateTime now)
    {
        var lesson = placement.Lesson;
        _time.Text = $"{lesson.StartTime:hh\\:mm} - {lesson.EndTime:hh\\:mm}";
        _name.Text = lesson.Name;
        _description.Text = lesson.Description;
        _description.IsVisible = !string.IsNullOrWhiteSpace(lesson.Description);
        BackgroundColor = LessonColor.Convert(lesson.Type, typeof(Color), null, CultureInfo.InvariantCulture) as Color ?? Colors.Gray;
        bool current = day.Date == now.Date && now.TimeOfDay >= lesson.StartTime && now.TimeOfDay < lesson.EndTime;
        StrokeThickness = current ? 3 : 0;
        Stroke = current ? Colors.Red : Colors.Transparent;
        Opacity = day.Date == now.Date && now.TimeOfDay >= lesson.EndTime ? 0.5 : 1;
        StyleId = current ? "CurrentLessonAnchor" : null;
        ContextActions.SetTapCommand(_text, day.ViewLessonCommand);
        ContextActions.SetMenuCommand(_text, day.LessonActionsCommand);
        ContextActions.SetParameter(_text, lesson);
        _menu.Command = day.LessonActionsCommand;
        _menu.CommandParameter = lesson;
    }
}
