using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class GroupSelectionPage : ContentPage
{
    public GroupSelectionPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is GroupSelectionViewModel vm)
        {
            SafeFireAndForget.Run(vm.LoadDataAsync);
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is GroupSelectionViewModel vm)
        {
            if (vm.IsLoadingGroups || vm.IsProcessing) return true;
        }
        return base.OnBackButtonPressed();
    }

    private void OnCategoryTapped(object? sender, TappedEventArgs e)
    {
        if (sender is View view && view.BindingContext is Models.GroupCategory category
            && BindingContext is GroupSelectionViewModel vm)
        {
            vm.ToggleCategoryCommand.Execute(category);
        }
    }

    private void OnGroupTapped(object? sender, TappedEventArgs e)
    {
        if (sender is View view && view.BindingContext is Models.GroupItem group
            && BindingContext is GroupSelectionViewModel vm)
        {
            vm.SelectGroupCommand.Execute(group);
        }
    }
}