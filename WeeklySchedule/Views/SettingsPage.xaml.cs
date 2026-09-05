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
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is SettingsViewModel vm)
        {
            vm.PropertyChanged += Vm_PropertyChanged;
            SafeFireAndForget.Run(async () =>
            {
                await vm.RefreshAsync();
                UpdatePickerVisibility(vm.IsStartupPickerVisible, false);
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
            UpdatePickerVisibility(vm.IsStartupPickerVisible, true);
    }

    private void UpdatePickerVisibility(bool isVisible, bool animate)
    {
        if (!animate)
        {
            StartupPickerLayout.IsVisible = isVisible;
            StartupPickerLayout.Opacity = isVisible ? 1 : 0;
            return;
        }

        SafeFireAndForget.Run(async () =>
        {
            if (isVisible)
            {
                StartupPickerLayout.IsVisible = true;
                await StartupPickerLayout.FadeToAsync(1, 250, Easing.CubicOut);
            }
            else
            {
                await StartupPickerLayout.FadeToAsync(0, 250, Easing.CubicIn);
                StartupPickerLayout.IsVisible = false;
            }
        });
    }
}
