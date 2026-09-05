using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;
using WeeklySchedule.Views;

namespace WeeklySchedule;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    private void OnHeaderDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (EditLessonPage.IsOpen) return;
        if (_viewModel.SelectedDayVM == null) return;

        SafeFireAndForget.Run(async () =>
        {
            var editPage = new EditLessonPage(
                lesson: null,
                preselectedDay: _viewModel.SelectedDayVM.DayOfWeek,
                activeTimelineId: _viewModel.ActiveTimelineId);
            await EditLessonPage.OpenModalAsync(editPage);
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.CheckPendingNavigation();

        SafeFireAndForget.Run(async () =>
        {
            await _viewModel.InitializeDataAsync();

            _viewModel.SelectedDayVM?.RequestScroll();

            if (Shell.Current is AppShell shell && shell.FlyoutVM != null)
            {
                await shell.FlyoutVM.LoadTimelinesAsync();
            }
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopMonitor();
    }
}