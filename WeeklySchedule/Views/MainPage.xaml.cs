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

    private async void OnHeaderDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (EditLessonPage.IsOpen) return;
        if (_viewModel.SelectedDayVM != null)
        {
            var editPage = new EditLessonPage(
                lesson: null,
                preselectedDay: _viewModel.SelectedDayVM.DayOfWeek,
                activeTimelineId: _viewModel.ActiveTimelineId);
            await EditLessonPage.OpenModalAsync(editPage);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.CheckPendingNavigation();

        // ИСПРАВЛЕНО: Асинхронный вызов инициализации
        await _viewModel.InitializeDataAsync();

        _viewModel.SelectedDayVM?.RequestScroll();

        if (Shell.Current is AppShell shell && shell.FlyoutVM != null)
        {
            // ИСПРАВЛЕНО: Асинхронный вызов загрузки таймлайнов
            await shell.FlyoutVM.LoadTimelinesAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopMonitor();
    }
}