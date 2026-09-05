using System.Globalization;
using WeeklySchedule.Models;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class DayView : ContentView
{
    private readonly DayViewSubscription _subscription;
    private readonly Dictionary<Guid, LessonCardView> _cards = [];
    private readonly List<BoxView> _separators = [];
    private readonly Label _empty = new()
    {
        Text = "Свободный день", FontSize = 24, FontAttributes = FontAttributes.Italic,
        TextColor = Colors.Gray, HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center, InputTransparent = true
    };
    private static readonly Converters.SeparatorTypeToColorConverter SeparatorColor = new();
    private static readonly Converters.SeparatorTypeToHeightConverter SeparatorHeight = new();
    private TimelineLayout? _renderedLayout;
    private double _screenHeight, _pixelsPerMinute, _maxElementHeight, _totalHeight;
    private double? _restoreY;
    private bool _scrollQueued, _scrollRunning, _scrollToCurrent;

    public DayView()
    {
        InitializeComponent();
        _subscription = new DayViewSubscription(OnLayoutUpdated, OnScrollToCurrentRequested);
        BindingContextChanged += (_, _) =>
        {
            _scrollToCurrent = false;
            _restoreY = null;
            if (IsLoaded) BindDay();
        };
        Loaded += (_, _) => BindDay();
        Unloaded += (_, _) => _subscription.Dispose();
        MainScroll.SizeChanged += (_, _) => QueueScroll();
        SizeChanged += (_, _) => { if (IsLoaded) OnLayoutUpdated(); };
    }

    private void BindDay()
    {
        _subscription.SetSource(BindingContext as DayViewModel);
        OnLayoutUpdated();
        if (BindingContext is DayViewModel { ScrollRequested: true }) OnScrollToCurrentRequested();
    }

    private void OnLayoutUpdated()
    {
        if (BindingContext is not DayViewModel day) return;
        var layout = day.Layout;
        var display = DeviceDisplay.MainDisplayInfo;
        var screenHeight = display.Height / display.Density;
        bool structureChanged = !ReferenceEquals(_renderedLayout, layout) || _screenHeight != screenHeight;

        if (structureChanged)
        {
            _restoreY = MainScroll.ScrollY;
            _screenHeight = screenHeight;
            _renderedLayout = layout;
            _maxElementHeight = (screenHeight - 120) * 0.5;
            if (_maxElementHeight < 200) _maxElementHeight = 400;
            int shortest = layout.Lessons.Count > 0 ? layout.Lessons.Min(l => l.TotalMinutes) : 15;
            _pixelsPerMinute = 90.0 / Math.Max(1, shortest);

            var ids = layout.Lessons.Select(p => p.Lesson.Id).ToHashSet();
            foreach (var id in _cards.Keys.Where(id => !ids.Contains(id)).ToArray())
            {
                TimelineGrid.Children.Remove(_cards[id]);
                _cards.Remove(id);
            }

            bool empty = layout.TotalMinutes == 0 || layout.Segments.Count == 0;
            TimelineGrid.VerticalOptions = empty ? LayoutOptions.Center : LayoutOptions.Start;
            TimelineGrid.MinimumHeightRequest = -1;
            if (empty)
            {
                TimelineGrid.RowDefinitions.Clear();
                TimelineGrid.ColumnDefinitions.Clear();
                TimelineGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                TimelineGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                if (!TimelineGrid.Children.Contains(_empty)) TimelineGrid.Children.Add(_empty);
                TimelineGrid.HeightRequest = -1;
                _totalHeight = 0;
                _restoreY = 0;
            }
            else
            {
                TimelineGrid.Children.Remove(_empty);
                while (TimelineGrid.RowDefinitions.Count > layout.Segments.Count)
                    TimelineGrid.RowDefinitions.RemoveAt(TimelineGrid.RowDefinitions.Count - 1);
                _totalHeight = 0;
                for (int i = 0; i < layout.Segments.Count; i++)
                {
                    double height = Math.Min(layout.Segments[i].DurationMinutes * _pixelsPerMinute, _maxElementHeight);
                    if (i == TimelineGrid.RowDefinitions.Count) TimelineGrid.RowDefinitions.Add(new RowDefinition());
                    TimelineGrid.RowDefinitions[i].Height = new GridLength(height);
                    _totalHeight += height;
                }
                int columns = Math.Max(1, layout.TotalColumns);
                while (TimelineGrid.ColumnDefinitions.Count > columns)
                    TimelineGrid.ColumnDefinitions.RemoveAt(TimelineGrid.ColumnDefinitions.Count - 1);
                while (TimelineGrid.ColumnDefinitions.Count < columns)
                    TimelineGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                TimelineGrid.ColumnSpacing = 6;
                TimelineGrid.HeightRequest = _totalHeight;
                foreach (var placement in layout.Lessons)
                {
                    if (!_cards.TryGetValue(placement.Lesson.Id, out var card))
                    {
                        card = new LessonCardView();
                        _cards.Add(placement.Lesson.Id, card);
                        TimelineGrid.Children.Add(card);
                    }
                    card.HeightRequest = Math.Min(placement.TotalMinutes * _pixelsPerMinute, _maxElementHeight);
                    card.VerticalOptions = LayoutOptions.Fill;
                    Grid.SetRow(card, placement.StartRow);
                    Grid.SetRowSpan(card, placement.RowSpan);
                    Grid.SetColumn(card, placement.Column);
                    Grid.SetColumnSpan(card, placement.ColumnSpan);
                }
            }
        }

        var now = TimeContext.Now;
        foreach (var placement in layout.Lessons)
            if (_cards.TryGetValue(placement.Lesson.Id, out var card)) card.Update(placement, day, now);
        UpdateBreaks(layout);
        if (structureChanged) QueueScroll();
    }

    private void UpdateBreaks(TimelineLayout layout)
    {
        while (_separators.Count > layout.Breaks.Count)
        {
            TimelineGrid.Children.Remove(_separators[^1]);
            _separators.RemoveAt(_separators.Count - 1);
        }
        for (int i = 0; i < layout.Breaks.Count; i++)
        {
            if (i == _separators.Count)
            {
                var line = new BoxView { VerticalOptions = LayoutOptions.Start, InputTransparent = true };
                _separators.Add(line);
                TimelineGrid.Children.Add(line);
            }
            var br = layout.Breaks[i];
            var separator = _separators[i];
            separator.Color = (Color)(SeparatorColor.Convert(br.Type, typeof(Color), string.Empty, CultureInfo.InvariantCulture) ?? Colors.Transparent);
            separator.HeightRequest = (double)(SeparatorHeight.Convert(br.Type, typeof(double), string.Empty, CultureInfo.InvariantCulture) ?? 0.0);
            separator.Margin = new Thickness(10, Math.Min(br.TotalMinutes * _pixelsPerMinute, _maxElementHeight) / 2 - 1, 10, 0);
            Grid.SetRow(separator, br.StartRow);
            Grid.SetRowSpan(separator, br.RowSpan);
            Grid.SetColumnSpan(separator, Math.Max(1, layout.TotalColumns));
        }
    }

    private void OnScrollToCurrentRequested()
    {
        _scrollToCurrent = true;
        QueueScroll();
    }

    // Один запрос после разметки: автопереход имеет приоритет над восстановлением.
    private void QueueScroll()
    {
        if (_scrollQueued || _scrollRunning || !IsLoaded || (!_scrollToCurrent && !_restoreY.HasValue)) return;
        _scrollQueued = true;
        Dispatcher.Dispatch(() =>
        {
            _scrollQueued = false;
            if (!IsLoaded || MainScroll.Height <= 0) return; // SizeChanged повторит запрос.
            SafeFireAndForget.Run(ApplyScrollAsync);
        });
    }

    private async Task ApplyScrollAsync()
    {
        if (_scrollRunning || BindingContext is not DayViewModel day) return;
        _scrollRunning = true;
        try
        {
            bool requested = _scrollToCurrent;
            double? target = _restoreY;
            _scrollToCurrent = false;
            _restoreY = null;
            if (requested)
            {
                day.AcknowledgeScroll();
                var anchor = _cards.Values.FirstOrDefault(c => c.StyleId == "CurrentLessonAnchor");
                if (anchor != null)
                {
                    double top = TimelineGrid.RowDefinitions.Take(Grid.GetRow(anchor)).Sum(r => r.Height.Value);
                    target = top + anchor.HeightRequest / 2 - MainScroll.Height / 2;
                }
            }
            if (target.HasValue)
                await MainScroll.ScrollToAsync(0, Math.Clamp(target.Value, 0, Math.Max(0, _totalHeight - MainScroll.Height)), requested);
        }
        finally
        {
            _scrollRunning = false;
            QueueScroll();
        }
    }
}
