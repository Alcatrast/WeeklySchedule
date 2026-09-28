using System.Globalization;
using WeeklySchedule.Models;
using WeeklySchedule.Utilities;
using WeeklySchedule.Resources.Strings;

namespace WeeklySchedule.Views;

public partial class TimelineGridView : Grid
{
    private static readonly Converters.SeparatorTypeToColorConverter SeparatorColor = new();
    private static readonly Converters.SeparatorTypeToHeightConverter SeparatorHeight = new();
    private static readonly Converters.LessonTypeToColorConverter LessonColor = new();

    public static readonly BindableProperty TimelineLayoutDataProperty =
        BindableProperty.Create(nameof(TimelineLayoutData), typeof(TimelineLayout), typeof(TimelineGridView), propertyChanged: OnLayoutChanged);

    public TimelineLayout TimelineLayoutData
    {
        get => (TimelineLayout)GetValue(TimelineLayoutDataProperty);
        set => SetValue(TimelineLayoutDataProperty, value);
    }

    public DateTime CurrentDate { get; set; } = DateTime.Today;

    private static void OnLayoutChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is TimelineGridView view && newValue is TimelineLayout layout)
            view.BuildTimeline(layout);
    }

    private void BuildTimeline(TimelineLayout layout)
    {
        Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();

        var displayInfo = DeviceDisplay.MainDisplayInfo;
        double screenHeightDp = displayInfo.Height / displayInfo.Density;

        if (layout.TotalMinutes == 0 || layout.Segments.Count == 0)
        {
            VerticalOptions = LayoutOptions.Center;
            HeightRequest = -1;
            MinimumHeightRequest = -1;
            RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Auto)));
            ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var freeDayLabel = new Label
            {
                Text = AppResources.FreeDay,
                Style = Application.Current?.Resources["Subtitle"] as Style,
                FontAttributes = FontAttributes.Italic,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            };
            Children.Add(freeDayLabel);
            return;
        }

        VerticalOptions = LayoutOptions.Start;
        double availableHeight = screenHeightDp - 120;
        double maxElementHeight = availableHeight * 0.5;
        if (maxElementHeight < 200) maxElementHeight = 400;

        int minLessonMinutes = layout.Lessons.Count > 0 ? layout.Lessons.Min(l => l.TotalMinutes) : 15;
        if (minLessonMinutes <= 0) minLessonMinutes = 15;

        const double StandardMinCardHeight = 90.0;
        double dynamicPixelsPerMinute = StandardMinCardHeight / minLessonMinutes;
        double totalGridHeight = 0;

        foreach (var seg in layout.Segments)
        {
            double rowHeight = seg.DurationMinutes * dynamicPixelsPerMinute;
            if (rowHeight > maxElementHeight) rowHeight = maxElementHeight;
            RowDefinitions.Add(new RowDefinition(new GridLength(rowHeight, GridUnitType.Absolute)));
            totalGridHeight += rowHeight;
        }

        int cols = Math.Max(1, layout.TotalColumns);
        for (int i = 0; i < cols; i++) ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        ColumnSpacing = 8;
        RowSpacing = 0;
        HeightRequest = totalGridHeight;

        var separatorStyle = Application.Current?.Resources["SeparatorBorder"] as Style;

        foreach (var br in layout.Breaks)
        {
            if (br.Type == SeparatorType.None) continue;
            double breakHeight = br.TotalMinutes * dynamicPixelsPerMinute;
            if (breakHeight > maxElementHeight) breakHeight = maxElementHeight;
            double lineOffset = (breakHeight / 2.0) - 2.0;

            var separatorLine = new Border
            {
                Style = separatorStyle,
                HeightRequest = (double)(SeparatorHeight.Convert(br.Type, typeof(double), string.Empty, CultureInfo.InvariantCulture) ?? 4.0),
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(16, lineOffset, 16, 0)
            };
            SetRow((IView)separatorLine, br.StartRow);
            SetRowSpan((IView)separatorLine, br.RowSpan);
            SetColumn((IView)separatorLine, 0);
            SetColumnSpan((IView)separatorLine, cols);
            Children.Add(separatorLine);
        }

        var now = TimeContext.Now;
        View? targetAnchor = null;
        TimeSpan nowTime = now.TimeOfDay;
        TimeSpan earliestFutureStart = TimeSpan.MaxValue;
        bool isCurrentDay = now.Date == CurrentDate;

        var accentBrush = Application.Current?.Resources["AccentBrush"] as SolidColorBrush;
        var accentColor = accentBrush?.Color ?? Colors.Blue;

        foreach (var lp in layout.Lessons)
        {
            var lessonCard = CreateLessonCard(lp, now, accentColor);
            if (isCurrentDay)
            {
                bool isCurrent = now.TimeOfDay >= lp.Lesson.StartTime && now.TimeOfDay < lp.Lesson.EndTime;
                if (isCurrent)
                {
                    targetAnchor = lessonCard;
                }
                else if (targetAnchor == null && lp.Lesson.StartTime > nowTime && lp.Lesson.StartTime < earliestFutureStart)
                {
                    earliestFutureStart = lp.Lesson.StartTime;
                    targetAnchor = lessonCard;
                }
            }

            double cardHeight = lp.TotalMinutes * dynamicPixelsPerMinute;
            if (cardHeight > maxElementHeight) cardHeight = maxElementHeight;

            lessonCard.HeightRequest = cardHeight;
            lessonCard.VerticalOptions = LayoutOptions.Fill;

            SetRow((IView)lessonCard, lp.StartRow);
            SetRowSpan((IView)lessonCard, lp.RowSpan);
            SetColumn((IView)lessonCard, lp.Column);
            SetColumnSpan((IView)lessonCard, lp.ColumnSpan);
            Children.Add(lessonCard);
        }

        targetAnchor?.StyleId = "CurrentLessonAnchor";
    }

    private Border CreateLessonCard(LessonPlacement lp, DateTime now, Color accentColor)
    {
        bool isCurrent = now.TimeOfDay >= lp.Lesson.StartTime && now.TimeOfDay < lp.Lesson.EndTime && now.Date == CurrentDate;
        bool isPast = now.TimeOfDay >= lp.Lesson.EndTime && now.Date == CurrentDate;

        var bgColor = LessonColor.Convert(lp.Lesson.Type, typeof(Color), string.Empty, CultureInfo.InvariantCulture) as Color ?? Colors.Gray;

        var border = new Border
        {
            BackgroundColor = bgColor,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            StrokeThickness = isCurrent ? 3 : 0,
            Stroke = isCurrent ? accentColor : Colors.Transparent,
            Padding = new Thickness(16),
            Margin = new Thickness(4, 4)
        };

        if (isPast) border.Opacity = 0.6;

        var stack = new VerticalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 4, 0, 0) };
        var culture = CultureInfo.CurrentCulture;

        var startTime = DateTime.Today.Add(lp.Lesson.StartTime);
        var endTime = DateTime.Today.Add(lp.Lesson.EndTime);
        string timeStr = $"{startTime.ToString("t", culture)} - {endTime.ToString("t", culture)}";

        stack.Children.Add(new Label
        {
            Text = timeStr,
            Style = Application.Current?.Resources["Caption"] as Style,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.NoWrap
        });

        stack.Children.Add(new Label
        {
            Text = lp.Lesson.Name,
            Style = Application.Current?.Resources["BodyBold"] as Style,
            LineBreakMode = LineBreakMode.TailTruncation
        });

        if (!string.IsNullOrWhiteSpace(lp.Lesson.Description))
        {
            stack.Children.Add(new Label
            {
                Text = lp.Lesson.Description,
                Style = Application.Current?.Resources["Caption"] as Style,
                LineBreakMode = LineBreakMode.TailTruncation
            });
        }

        border.Content = stack;

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += (s, e) =>
        {
            if (BindingContext is ViewModels.DayViewModel vm && vm.EditLessonCommand.CanExecute(lp.Lesson))
            {
                vm.EditLessonCommand.Execute(lp.Lesson);
            }
        };
        border.GestureRecognizers.Add(tapGesture);

        if (isCurrent)
        {
            border.StyleId = "CurrentLessonAnchor";
        }

        return border;
    }
}