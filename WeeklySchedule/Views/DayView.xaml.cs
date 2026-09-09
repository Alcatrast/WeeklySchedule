using Microsoft.Maui.Controls.Shapes;
using WeeklySchedule.Core;
using WeeklySchedule.Models;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class DayView : ContentView
{
    // Отступ надписи свободного дня от верха ленты. Сетка теперь общая для недели,
    // поэтому свободный день такой же высокий, как заполненный, и надпись по центру
    // всей высоты пришлось бы искать прокруткой.
    private const double EmptyLabelTopMargin = 48;

    private readonly DayViewSubscription _subscription;
    // Карточки переиспользуются по номеру места, как пометки и подписи «окон» ниже.
    // Пока они искались по идентификатору пары, смена дня не находила ни одной —
    // идентификаторы у другого дня другие, — и каждый свайп снимал с дерева все
    // карточки и строил новые: Border, Grid, три Label, Button и две подписки на
    // касания на каждую пару
    private readonly List<LessonCardView> _cards = [];
    private readonly List<BaseDayCardView> _markers = [];
    private readonly List<Label> _gapLabels = [];
    private readonly Label _empty = new()
    {
        Text = "Свободный день", FontSize = 24, FontAttributes = FontAttributes.Italic,
        TextColor = Colors.Gray, HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Start, InputTransparent = true
    };
    // Метка текущего времени: маленький треугольник у левого края, только на сегодня.
    private readonly Polygon _nowMarker = new()
    {
        Points = [new Point(0, 0), new Point(7, 4.5), new Point(0, 9)],
        Fill = new SolidColorBrush(Color.FromArgb("#E04A4A")),
        WidthRequest = 7, HeightRequest = 9,
        HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
        InputTransparent = true, IsVisible = false
    };
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
            // Карусель назначает день до подключения View к нативному дереву.
            // Создаём содержимое уже здесь: ожидание Loaded оставляло первый
            // показ пустым до повторного свайпа. Только прокрутке нужны размер
            // и IsLoaded; построение сетки от них не зависит.
            BindDay();
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
        try { RenderLayout(); }
        catch
        {
            // Незавершённую отрисовку нельзя переиспользовать по ReferenceEquals:
            // при следующем показе необходимо заново построить карточки и сетку.
            _renderedLayout = null;
            throw;
        }
    }

    private void RenderLayout()
    {
        if (BindingContext is not DayViewModel day) return;
        var layout = day.Layout;
        // Геометрия общая для всей недели: и масштаб, и разметка строк приходят
        // из WeekLayout, поэтому от размера экрана и от состава дня не зависят.
        bool structureChanged = !ReferenceEquals(_renderedLayout, layout);

        if (structureChanged)
        {
            _restoreY = MainScroll.ScrollY;
            _renderedLayout = layout;
            _rowHeights = layout.RowHeights;
            _totalHeight = TimelineMetrics.TotalHeight(_rowHeights);

            // «Пусто» — это пустая НЕДЕЛЯ, а не пустой день: день без пар внутри
            // непустой недели строит те же строки, что и все остальные, и занимает
            // столько же места
            bool weekEmpty = layout.Segments.Count == 0;
            TimelineGrid.VerticalOptions = weekEmpty ? LayoutOptions.Center : LayoutOptions.Start;
            TimelineGrid.MinimumHeightRequest = -1;
            int columns;
            if (weekEmpty)
            {
                TimelineGrid.RowDefinitions.Clear();
                TimelineGrid.ColumnDefinitions.Clear();
                TimelineGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                TimelineGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                TimelineGrid.HeightRequest = -1;
                columns = 1;
                _totalHeight = 0;
                _restoreY = 0;
            }
            else
            {
                // Строк на одну больше, чем сегментов: последняя — распорка Star.
                // Если MAUI когда-нибудь растянет содержимое ScrollView до высоты
                // вьюпорта, избыток уйдет в нее, а не размажется по строкам пар.
                int rows = layout.Segments.Count + 1;
                while (TimelineGrid.RowDefinitions.Count > rows)
                    TimelineGrid.RowDefinitions.RemoveAt(TimelineGrid.RowDefinitions.Count - 1);
                while (TimelineGrid.RowDefinitions.Count < rows)
                    TimelineGrid.RowDefinitions.Add(new RowDefinition());
                // Только при отличии: высоты общие для всей недели, поэтому при свайпе
                // они те же самые, а каждое присваивание дергает пересчет всей сетки
                for (int i = 0; i < layout.Segments.Count; i++)
                {
                    var height = new GridLength(_rowHeights[i]);
                    if (TimelineGrid.RowDefinitions[i].Height != height)
                        TimelineGrid.RowDefinitions[i].Height = height;
                }
                if (TimelineGrid.RowDefinitions[rows - 1].Height != GridLength.Star)
                    TimelineGrid.RowDefinitions[rows - 1].Height = GridLength.Star;
                columns = Math.Max(1, layout.TotalColumns);
                while (TimelineGrid.ColumnDefinitions.Count > columns)
                    TimelineGrid.ColumnDefinitions.RemoveAt(TimelineGrid.ColumnDefinitions.Count - 1);
                while (TimelineGrid.ColumnDefinitions.Count < columns)
                    TimelineGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                TimelineGrid.ColumnSpacing = 6;
                TimelineGrid.HeightRequest = _totalHeight;
                for (int i = 0; i < layout.Lessons.Count; i++)
                {
                    var placement = layout.Lessons[i];
                    var card = CardAt(i);
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

            TrimCards(weekEmpty ? 0 : layout.Lessons.Count);
            UpdateMarkers(layout, columns);
            UpdateGapLabels(layout, weekEmpty);
            UpdateEmptyLabel(layout, weekEmpty, columns);
            // Треугольник кладется последним: порядок детей в Grid и есть z-order, а
            // непрозрачный фон карточки, добавленной позже, закрыл бы метку. Проверка
            // на месте: обычно он и так последний, а перестановка снимает и заново
            // цепляет нативный элемент
            if (TimelineGrid.Children.Count == 0 ||
                !ReferenceEquals(TimelineGrid.Children[^1], _nowMarker))
            {
                TimelineGrid.Children.Remove(_nowMarker);
                TimelineGrid.Children.Add(_nowMarker);
            }
            Grid.SetRow(_nowMarker, 0);
            Grid.SetRowSpan(_nowMarker, Math.Max(1, layout.Segments.Count));
        }

        var now = TimeContext.Now;
        for (int i = 0; i < layout.Lessons.Count && i < _cards.Count; i++)
            _cards[i].Update(layout.Lessons[i], day, now);
        UpdateNowMarker(layout);
        if (structureChanged) QueueScroll();
    }

    private LessonCardView CardAt(int index)
    {
        if (index < _cards.Count) return _cards[index];
        var created = new LessonCardView();
        _cards.Add(created);
        TimelineGrid.Children.Add(created);
        return created;
    }

    private void TrimCards(int used)
    {
        while (_cards.Count > used)
        {
            TimelineGrid.Children.Remove(_cards[^1]);
            _cards.RemoveAt(_cards.Count - 1);
        }
    }

    // Базовые дни рисуются под карточками пар: пометка на весь день накрывает всю
    // сетку, и поверх нее должны читаться пары. Порядок детей в Grid и есть z-order,
    // поэтому новые блоки вставляются в начало списка, а не добавляются в конец.
    private void UpdateMarkers(TimelineLayout layout, int columns)
    {
        int used = 0;
        foreach (var placement in layout.Markers)
        {
            if (used == _markers.Count)
            {
                var created = new BaseDayCardView();
                _markers.Add(created);
                TimelineGrid.Children.Insert(0, created);
            }
            var block = _markers[used++];
            block.Update(placement);
            Grid.SetRow(block, placement.StartRow);
            Grid.SetRowSpan(block, placement.RowSpan);
            Grid.SetColumn(block, 0);
            Grid.SetColumnSpan(block, columns);
        }
        while (_markers.Count > used)
        {
            TimelineGrid.Children.Remove(_markers[^1]);
            _markers.RemoveAt(_markers.Count - 1);
        }
    }

    // Надпись показывается, только когда в дне нет ни пар, ни пометок: базовый день —
    // не свободный день, и раньше он получал обе подписи сразу.
    private void UpdateEmptyLabel(TimelineLayout layout, bool weekEmpty, int columns)
    {
        bool show = layout.Lessons.Count == 0 && layout.Markers.Count == 0;
        if (!show)
        {
            TimelineGrid.Children.Remove(_empty);
            return;
        }
        if (!TimelineGrid.Children.Contains(_empty)) TimelineGrid.Children.Add(_empty);
        Grid.SetRow(_empty, 0);
        Grid.SetRowSpan(_empty, Math.Max(1, layout.Segments.Count));
        Grid.SetColumn(_empty, 0);
        Grid.SetColumnSpan(_empty, columns);
        _empty.VerticalOptions = weekEmpty ? LayoutOptions.Center : LayoutOptions.Start;
        _empty.Margin = weekEmpty ? default : new Thickness(0, EmptyLabelTopMargin, 0, 0);
    }

    // Подписи длинных «окон». От текущего времени не зависят, поэтому живут рядом
    // с геометрией. Окно берется из общего для недели GapRows: по одному дню каждая
    // строка свободного дня оказалась бы «окном».
    private void UpdateGapLabels(TimelineLayout layout, bool weekEmpty)
    {
        int used = 0;
        if (!weekEmpty)
        {
            for (int row = 0; row < layout.Segments.Count; row++)
            {
                int minutes = layout.Segments[row].DurationMinutes;
                if (minutes < TimelineMetrics.GapLabelThreshold || !layout.GapRows[row]) continue;
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
        }
        while (_gapLabels.Count > used)
        {
            TimelineGrid.Children.Remove(_gapLabels[^1]);
            _gapLabels.RemoveAt(_gapLabels.Count - 1);
        }
    }

    // Треугольник стоит в строке 0 с рядами на всю сетку и сдвигается по вертикали:
    // так не нужно считать положение внутри своей строки и нельзя промахнуться
    // мимо ее границы. Сдвиг именно TranslationY, а не Margin: отступ меняет
    // раскладку, и метка, ползущая раз в минуту, каждый раз пересчитывала бы всю
    // сетку из нескольких десятков строк со всеми карточками.
    private void UpdateNowMarker(TimelineLayout layout)
    {
        if (layout.CurrentTimeOffset is not double offset)
        {
            _nowMarker.IsVisible = false;
            return;
        }
        _nowMarker.IsVisible = true;
        _nowMarker.TranslationY = offset - 4.5;
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
                var anchor = _cards.FirstOrDefault(c => c.StyleId == "CurrentLessonAnchor");
                if (anchor != null)
                {
                    int anchorRow = Grid.GetRow(anchor);
                    double top = TimelineMetrics.TopOffset(_rowHeights, anchorRow);
                    double height = TimelineMetrics.SpanHeight(_rowHeights, anchorRow, Grid.GetRowSpan(anchor));
                    target = top + height / 2 - MainScroll.Height / 2;
                }
            }
            if (target.HasValue)
            {
                var scroll = MainScroll.ScrollToAsync(0,
                    Math.Clamp(target.Value, 0, Math.Max(0, _totalHeight - MainScroll.Height)), requested);
                // ScrollToAsync завершается по событию окончания прокрутки. Карусель
                // переиспользует вью прямо во время анимации, и отцепленный от дерева
                // ScrollView такого события может не прислать — тогда ожидание не
                // вернулось бы никогда, finally ниже не выполнился бы, и этот день
                // больше не прокрутился бы ни к текущей паре, ни к прежней позиции
                await Task.WhenAny(scroll, Task.Delay(TimeSpan.FromSeconds(2)));
            }
        }
        finally
        {
            _scrollRunning = false;
            QueueScroll();
        }
    }
}
