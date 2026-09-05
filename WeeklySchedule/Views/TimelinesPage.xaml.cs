using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class TimelinesPage : ContentPage
{
    private readonly TimelinesViewModel _viewModel;

    public TimelinesPage(TimelinesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SafeFireAndForget.Run(_viewModel.LoadTimelinesAsync);
    }
}