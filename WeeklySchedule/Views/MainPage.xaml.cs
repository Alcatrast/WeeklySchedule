using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly IEditLessonPageFactory _pageFactory;

    public MainPage(MainViewModel viewModel, IEditLessonPageFactory pageFactory)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _pageFactory = pageFactory;
        BindingContext = _viewModel;
    }

    private void OnHeaderDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_pageFactory.IsOpen) return;
        if (_viewModel.SelectedDayVM == null) return;
        SafeFireAndForget.Run(() => _pageFactory.OpenAsync(preselectedDay: _viewModel.SelectedDayVM.DayOfWeek, activeTimelineId: _viewModel.ActiveTimelineId));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.CheckPendingNavigation();
        SafeFireAndForget.Run(async () =>
        {
            await _viewModel.InitializeDataAsync();
            _viewModel.SelectedDayVM?.RequestScroll();
            if (Shell.Current is AppShell shell && shell.FlyoutVM != null) await shell.FlyoutVM.LoadTimelinesAsync();
        });
    }

    protected override void OnDisappearing() { base.OnDisappearing(); _viewModel.StopMonitor(); }
}