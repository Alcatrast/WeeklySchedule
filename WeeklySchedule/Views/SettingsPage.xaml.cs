using System.ComponentModel;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is SettingsViewModel vm)
        {
            vm.PropertyChanged += Vm_PropertyChanged;
            await vm.CheckAllPermissionsAsync();
            UpdatePickerVisibility(vm.IsStartupPickerVisible, false);
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
            UpdatePickerVisibility(vm.IsStartupPickerVisible, true);
    }

    private async void UpdatePickerVisibility(bool isVisible, bool animate)
    {
        if (animate)
        {
            if (isVisible) { StartupPickerLayout.IsVisible = true; await StartupPickerLayout.FadeToAsync(1, 250, Easing.CubicOut); }
            else { await StartupPickerLayout.FadeToAsync(0, 250, Easing.CubicIn); StartupPickerLayout.IsVisible = false; }
        }
        else
        {
            StartupPickerLayout.IsVisible = isVisible;
            StartupPickerLayout.Opacity = isVisible ? 1 : 0;
        }
    }
}