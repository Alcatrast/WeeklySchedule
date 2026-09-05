using System.Globalization;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class DayView : ContentView
{
    // Конвертеры без состояния: раньше создавались заново на каждый разделитель
    // и на каждую карточку пары внутри цикла перерисовки
    private static readonly Converters.SeparatorTypeToColorConverter SeparatorColor = new();
    private static readonly Converters.SeparatorTypeToHeightConverter SeparatorHeight = new();
    private static readonly Converters.LessonTypeToColorConverter LessonColor = new();

    public DayView()
    {
        InitializeComponent();

        var tapGesture = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        tapGesture.Tapped += OnBackgroundDoubleTapped;

        MainScroll.GestureRecognizers.Add(tapGesture);

        BindingContextChanged += (_, _) =>
        {
            if (BindingContext is DayViewModel vm)
            {
                vm.LayoutUpdated -= OnLayoutUpdated;
                vm.LayoutUpdated += OnLayoutUpdated;
                vm.ScrollToCurrentRequested -= OnScrollToCurrentRequested;
                vm.ScrollToCurrentRequested += OnScrollToCurrentRequested;

                OnLayoutUpdated();
            }
        };
    }

    private void OnLayoutUpdated()
    {
        if (BindingContext is not DayViewModel vm) return;
        var layout = vm.Layout;
        double savedScrollY = MainScroll.ScrollY;

        TimelineGrid.Children.Clear();
        TimelineGrid.RowDefinitions.Clear();
        TimelineGrid.ColumnDefinitions.Clear();

        var displayInfo = DeviceDisplay.MainDisplayInfo;
        double screenHeightDp = displayInfo.Height / displayInfo.Density;


        if (layout.TotalMinutes == 0 || layout.Segments.Count == 0)
        {
            TimelineGrid.Children.Clear();
            TimelineGrid.RowDefinitions.Clear();
            TimelineGrid.ColumnDefinitions.Clear();

            // Сбрасываем жесткие высоты и позволяем Grid центрироваться в ScrollView
            TimelineGrid.VerticalOptions = LayoutOptions.Center;
            TimelineGrid.HeightRequest = -1;
            TimelineGrid.MinimumHeightRequest = -1;

            TimelineGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Auto)));
            TimelineGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var emptyLabel = new Label
            {
                Text = "Свободный день",
                FontSize = 24,
                FontAttributes = FontAttributes.Italic,
                TextColor = Colors.Gray,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            };
            TimelineGrid.Children.Add(emptyLabel);

            Dispatcher.Dispatch(() => MainScroll.ScrollToAsync(0, 0, false));
            this.InvalidateMeasure();
            return;
        }

        TimelineGrid.HeightRequest = -1;
        TimelineGrid.VerticalOptions = LayoutOptions.Start;

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
            TimelineGrid.RowDefinitions.Add(new RowDefinition(new GridLength(rowHeight, GridUnitType.Absolute)));
            totalGridHeight += rowHeight;
        }

        int cols = Math.Max(1, layout.TotalColumns);
        for (int i = 0; i < cols; i++)
        {
            TimelineGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        }

        TimelineGrid.ColumnSpacing = 6;
        TimelineGrid.RowSpacing = 0;
        TimelineGrid.HeightRequest = totalGridHeight;

        foreach (var br in layout.Breaks)
        {
            if (br.Type == SeparatorType.None) continue;

            double breakHeight = br.TotalMinutes * dynamicPixelsPerMinute;
            if (breakHeight > maxElementHeight) breakHeight = maxElementHeight;
            double lineOffset = (breakHeight / 2.0) - 1.0;

            var separatorLine = new BoxView
            {
                Color = (Color)(SeparatorColor.Convert(br.Type, typeof(Color), string.Empty, CultureInfo.InvariantCulture) ?? Colors.Transparent),
                HeightRequest = (double)(SeparatorHeight.Convert(br.Type, typeof(double), string.Empty, CultureInfo.InvariantCulture) ?? 0.0),
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(10, lineOffset, 10, 0)
            };

            Grid.SetRow(separatorLine, br.StartRow);
            Grid.SetRowSpan(separatorLine, br.RowSpan);
            Grid.SetColumn(separatorLine, 0);
            Grid.SetColumnSpan(separatorLine, cols);
            TimelineGrid.Children.Add(separatorLine);
        }

        var now = TimeContext.Now;
        foreach (var lp in layout.Lessons)
        {
            var lessonCard = CreateLessonCard(lp, vm, now);

            double cardHeight = lp.TotalMinutes * dynamicPixelsPerMinute;
            if (cardHeight > maxElementHeight) cardHeight = maxElementHeight;

            lessonCard.HeightRequest = cardHeight;
            lessonCard.VerticalOptions = LayoutOptions.Fill;
            Grid.SetRow(lessonCard, lp.StartRow);
            Grid.SetRowSpan(lessonCard, lp.RowSpan);
            Grid.SetColumn(lessonCard, lp.Column);
            Grid.SetColumnSpan(lessonCard, lp.ColumnSpan);
            TimelineGrid.Children.Add(lessonCard);
        }

        TimelineGrid.InvalidateMeasure();
        MainScroll.InvalidateMeasure();
        this.InvalidateMeasure();

        Dispatcher.Dispatch(() =>
        {
            double scrollViewHeight = MainScroll.Height > 0 ? MainScroll.Height : screenHeightDp;
            if (totalGridHeight <= scrollViewHeight)
            {
                MainScroll.ScrollToAsync(0, 0, false);
            }
            else
            {
                double maxScrollY = Math.Max(0, totalGridHeight - scrollViewHeight);
                double targetScrollY = Math.Min(savedScrollY, maxScrollY);
                MainScroll.ScrollToAsync(0, targetScrollY, false);
            }
        });
    }

    private static Border CreateLessonCard(LessonPlacement lp, DayViewModel vm, DateTime now)
    {
        bool isCurrent = now.TimeOfDay >= lp.Lesson.StartTime && now.TimeOfDay < lp.Lesson.EndTime && now.Date == vm.Date;
        bool isPast = now.TimeOfDay >= lp.Lesson.EndTime && now.Date == vm.Date;

        var bgColor = LessonColor.Convert(lp.Lesson.Type, typeof(Color), string.Empty, CultureInfo.InvariantCulture) as Color ?? Colors.Gray;

        var border = new Border
        {
            BackgroundColor = bgColor,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            StrokeThickness = isCurrent ? 3 : 0,
            Stroke = isCurrent ? Colors.Red : Colors.Transparent,
            Padding = new Thickness(10, 10, 10, 10),
            Margin = new Thickness(3, 2)
        };

        if (isPast) border.Opacity = 0.5;

        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var timeLabel = new Label
        {
            Text = $"{lp.Lesson.StartTime:hh\\:mm} - {lp.Lesson.EndTime:hh\\:mm}",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            Opacity = 0.8,
            LineBreakMode = LineBreakMode.NoWrap
        };
        stack.Children.Add(timeLabel);

        var nameLabel = new Label
        {
            Text = lp.Lesson.Name,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        stack.Children.Add(nameLabel);

        if (!string.IsNullOrWhiteSpace(lp.Lesson.Description))
        {
            var descLabel = new Label
            {
                Text = lp.Lesson.Description,
                FontSize = 11,
                Opacity = 0.7,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            stack.Children.Add(descLabel);
        }

        border.Content = stack;

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += (s, e) =>
        {
            if (vm.EditLessonCommand.CanExecute(lp.Lesson))
                vm.EditLessonCommand.Execute(lp.Lesson);
        };
        border.GestureRecognizers.Add(tapGesture);

        if (isCurrent) border.StyleId = "CurrentLessonAnchor";

        return border;
    }

    private void OnScrollToCurrentRequested()
    {
        var anchor = TimelineGrid.Children
            .OfType<View>()
            .FirstOrDefault(v => v.StyleId == "CurrentLessonAnchor");

        if (anchor != null)
        {
            var scrollView = this.FindByName<ScrollView>("MainScroll");
            scrollView?.ScrollToAsync(anchor, ScrollToPosition.Center, true);
        }

    }

    private async void OnBackgroundDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (EditLessonPage.IsOpen) return;
        if (BindingContext is DayViewModel vm)
        {
            var scheduleService = Application.Current!.Handler!.MauiContext!.Services.GetRequiredService<IActiveScheduleService>();

            var editPage = new EditLessonPage(
                lesson: null,
                preselectedDay: vm.DayOfWeek,
                activeTimelineId: scheduleService.ActiveTimelineId); // Передаем ID

            await EditLessonPage.OpenModalAsync(editPage);
        }
    }
}