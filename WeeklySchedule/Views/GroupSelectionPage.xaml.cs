using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class GroupSelectionPage : ContentPage
{
    private readonly GroupSelectionViewModel _vm;

    public GroupSelectionPage(GroupSelectionViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SafeFireAndForget.Run(_vm.LoadDataAsync);
    }

    protected override bool OnBackButtonPressed()
    {
        if (_vm.IsLoadingGroups || _vm.IsProcessing) return true;
        return base.OnBackButtonPressed();
    }

    private void OnCategoryTapped(object? sender, TappedEventArgs e)
    {
        if (sender is View view && view.BindingContext is Models.GroupCategory category)
        {
            _vm.ToggleCategoryCommand.Execute(category);
        }
    }

    private void OnGroupTapped(object? sender, TappedEventArgs e)
    {
        if (sender is View view && view.BindingContext is Models.GroupItem group)
        {
            _vm.SelectGroupCommand.Execute(group);
        }
    }
}