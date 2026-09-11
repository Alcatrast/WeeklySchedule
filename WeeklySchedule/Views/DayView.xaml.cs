using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class DayView : ContentView
{
    private readonly DayViewSubscription _subscription;

    public DayView()
    {
        InitializeComponent();
        _subscription = new DayViewSubscription(OnLayoutUpdated, OnScrollToCurrentRequested);
        BindingContextChanged += (_, _) => { if (IsLoaded) BindDay(); };
        Loaded += (_, _) => BindDay();
        Unloaded += (_, _) => _subscription.Dispose();
    }

    private void BindDay()
    {
        _subscription.SetSource(BindingContext as DayViewModel);
        OnLayoutUpdated();
    }

    private void OnLayoutUpdated()
    {
        if (BindingContext is not DayViewModel vm) return;

        TimelineGrid.CurrentDate = vm.Date;
        TimelineGrid.TimelineLayoutData = vm.Layout;

        Dispatcher.Dispatch(() =>
        {
            var displayInfo = DeviceDisplay.MainDisplayInfo;
            double screenHeightDp = displayInfo.Height / displayInfo.Density;
            double scrollViewHeight = MainScroll.Height > 0 ? MainScroll.Height : screenHeightDp;
            double totalGridHeight = TimelineGrid.HeightRequest > 0 ? TimelineGrid.HeightRequest : 0;

            if (totalGridHeight <= scrollViewHeight)
                MainScroll.ScrollToAsync(0, 0, false);
            else
            {
                double maxScrollY = Math.Max(0, totalGridHeight - scrollViewHeight);
                MainScroll.ScrollToAsync(0, Math.Min(MainScroll.ScrollY, maxScrollY), false);
            }
        });
    }

    private void OnScrollToCurrentRequested()
    {
        var anchor = TimelineGrid.Children.OfType<View>().FirstOrDefault(v => v.StyleId == "CurrentLessonAnchor");
        if (anchor != null) MainScroll.ScrollToAsync(anchor, ScrollToPosition.Center, true);
    }
}