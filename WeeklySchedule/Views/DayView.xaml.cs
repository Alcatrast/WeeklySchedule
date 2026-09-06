using System.Globalization;
using WeeklySchedule.Core;
using WeeklySchedule.Models;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class DayView : ContentView
{
    private readonly DayViewSubscription _subscription;
    private readonly Dictionary<Guid, LessonCardView> _cards = [];
    private readonly List<BoxView> _separators = [];
    private readonly List<Label> _gapLabels = [];
    private readonly Label _empty = new()
    {
        Text = "Свободный день", FontSize = 24, FontAttributes = FontAttributes.Italic,
        TextColor = Colors.Gray, HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center, InputTransparent = true
    };
    private static readonly Converters.SeparatorTypeToColorConverter SeparatorColor = new();
    private static readonly Converters.SeparatorTypeToHeightConverter SeparatorHeight = new();
    private TimelineLayout? _renderedLayout;
    private double[] _rowHeights = [];
    private double _totalHeight;
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
        // Геометрия больше не зависит от размера экрана: масштаб общий для всех дней.
        bool structureChanged = !ReferenceEquals(_renderedLayout, layout);

        if (structureChanged)
        {
            _restoreY = MainScroll.ScrollY;
            _renderedLayout = layout;
            _rowHeights = TimelineMetrics.RowHeights(layout);

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
                // Строк на одну больше, чем сегментов: последняя — распорка Star.
                // Если MAUI когда-нибудь растянет содержимое ScrollView до высоты
                // вьюпорта, избыток уйдет в нее, а не размажется по строкам пар.
                int rows = layout.Segments.Count + 1;
                while (TimelineGrid.RowDefinitions.Count > rows)
                    TimelineGrid.RowDefinitions.RemoveAt(TimelineGrid.RowDefinitions.Count - 1);
                while (TimelineGrid.RowDefinitions.Count < rows)
                    TimelineGrid.RowDefinitions.Add(new RowDefinition());
                _totalHeight = 0;
                for (int i = 0; i < layout.Segments.Count; i++)
                {
                    TimelineGrid.RowDefinitions[i].Height = new GridLength(_rowHeights[i]);
                    _totalHeight += _rowHeights[i];
                }
                TimelineGrid.RowDefinitions[rows - 1].Height = GridLength.Star;
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
                    // Android не всегда измеряет Border по высоте строк при RowSpan.
                    // Явная высота берется из тех же строк, поэтому карточка остается
                    // согласована с сеткой и не исчезает при нативной раскладке.
                    card.HeightRequest = TimelineMetrics.SpanHeight(
                        _rowHeights, placement.StartRow, placement.RowSpan);
                    card.VerticalOptions = LayoutOptions.Fill;
                    Grid.SetRow(card, placement.StartRow);
                    Grid.SetRowSpan(card, placement.RowSpan);
                    Grid.SetColumn(card, placement.Column);
                    Grid.SetColumnSpan(card, placement.ColumnSpan);
                }
            }
            UpdateGapLabels(layout);
        }

        var now = TimeContext.Now;
        foreach (var placement in layout.Lessons)
            if (_cards.TryGetValue(placement.Lesson.Id, out var card)) card.Update(placement, day, now);
        UpdateBreaks(layout);
        if (structureChanged) QueueScroll();
    }

    // Подписи длинных «окон». От текущего времени не зависят, поэтому живут рядом
    // с геометрией, а не в UpdateBreaks.
    private void UpdateGapLabels(TimelineLayout layout)
    {
        int used = 0;
        for (int row = 0; row < layout.Segments.Count; row++)
        {
            int minutes = layout.Segments[row].DurationMinutes;
            if (minutes < TimelineMetrics.GapLabelThreshold || !TimelineMetrics.IsGapRow(layout, row)) continue;
            if (used == _gapLabels.Count)
            {
                var created = new Label
                {
                    FontSize = 11, TextColor = Colors.Gray, InputTransparent = true,
                    HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
                };
                _gapLabels.Add(created);
                TimelineGrid.Children.Add(created);
            }
            var label = _gapLabels[used++];
            label.Text = TimelineMetrics.FormatGap(minutes);
            Grid.SetRow(label, row);
            Grid.SetRowSpan(label, 1);
            Grid.SetColumn(label, 0);
            Grid.SetColumnSpan(label, Math.Max(1, layout.TotalColumns));
        }
        while (_gapLabels.Count > used)
        {
            TimelineGrid.Children.Remove(_gapLabels[^1]);
            _gapLabels.RemoveAt(_gapLabels.Count - 1);
        }
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
            separator.Margin = new Thickness(10, TimelineMetrics.SpanHeight(_rowHeights, br.StartRow, br.RowSpan) / 2 - 1, 10, 0);
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
                    int anchorRow = Grid.GetRow(anchor);
                    double top = TimelineMetrics.TopOffset(_rowHeights, anchorRow);
                    double height = TimelineMetrics.SpanHeight(_rowHeights, anchorRow, Grid.GetRowSpan(anchor));
                    target = top + height / 2 - MainScroll.Height / 2;
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
