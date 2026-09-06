using WeeklySchedule.Models;

namespace WeeklySchedule.Views;

// Пометка расписания в ленте дня. Не пара: не кликается, не идет в уведомления,
// лежит под карточками пар и занимает всю ширину своих строк.
public sealed class BaseDayCardView : Border
{
    private readonly Label _text = new()
    {
        FontSize = 13, HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap, VerticalOptions = LayoutOptions.Start,
        InputTransparent = true
    };

    public BaseDayCardView()
    {
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 };
        StrokeThickness = 0;
        Padding = new Thickness(12, 6);
        Margin = new Thickness(3, 2);
        InputTransparent = true;
        this.SetAppThemeColor(BackgroundColorProperty,
            Color.FromArgb("#EEE9FA"), Color.FromArgb("#342C48"));
        _text.SetAppThemeColor(Label.TextColorProperty,
            Color.FromArgb("#51406E"), Color.FromArgb("#E4D8FF"));
        Content = _text;
    }

    public void Update(MarkerPlacement placement) => _text.Text = placement.Text;
}
