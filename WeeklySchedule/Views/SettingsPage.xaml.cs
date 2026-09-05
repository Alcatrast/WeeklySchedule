using System.ComponentModel;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        UpdatePickerVisibility(viewModel.IsStartupPickerVisible);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is SettingsViewModel vm)
        {
            vm.PropertyChanged -= Vm_PropertyChanged;
            vm.PropertyChanged += Vm_PropertyChanged;
            SafeFireAndForget.Run(async () =>
            {
                await vm.RefreshAsync();
                UpdatePickerVisibility(vm.IsStartupPickerVisible);
            });
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is SettingsViewModel vm) vm.PropertyChanged -= Vm_PropertyChanged;
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsStartupPickerVisible) && sender is SettingsViewModel vm)
            UpdatePickerVisibility(vm.IsStartupPickerVisible);
    }

    private void UpdatePickerVisibility(bool isVisible)
    {
        StartupPickerLayout.IsVisible = isVisible;
    }
}
