using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class GroupSelectionPage : ContentPage
{
    public GroupSelectionPage(
        string filePath,
        bool timelineExists,
        Timeline timeline,
        ILessonRepository lessonRepo,
        ITimelineRepository timelineRepo,
        INavigationService navigationService,
        IServiceProvider serviceProvider)
    {
        InitializeComponent();
        BindingContext = new GroupSelectionViewModel(filePath, timelineExists, timeline, lessonRepo, timelineRepo, navigationService, serviceProvider);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is GroupSelectionViewModel vm)
        {
            SafeFireAndForget.Run(vm.InitializeAsync);
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
        if (sender is View view && view.BindingContext is GroupCategory category && BindingContext is GroupSelectionViewModel vm)
        {
            vm.ToggleCategoryCommand.Execute(category);
        }
    }

    private void OnGroupTapped(object? sender, TappedEventArgs e)
    {
        if (sender is View view && view.BindingContext is GroupItem group && BindingContext is GroupSelectionViewModel vm)
        {
            vm.SelectGroupCommand.Execute(group);
        }
    }
}