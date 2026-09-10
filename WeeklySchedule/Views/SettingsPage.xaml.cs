using System.ComponentModel;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class SettingsPage : ContentPage
{
    /// <summary>
    /// Окно, на котором висит подписка. Хранится отдельно от свойства Window,
    /// чтобы отписка попала в тот же экземпляр, на который была подписка.
    /// </summary>
    private Window? _subscribedWindow;

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
            SubscribeToResume();
            SafeFireAndForget.Run(async () =>
            {
                await vm.RefreshIfStaleAsync();
                UpdatePickerVisibility(vm.IsStartupPickerVisible);
                // Страница переживает уход с нее, поэтому нечисловой текст остался бы в
                // поле навсегда: привязка его не преобразует, вью-модель не обновляется и
                // сама поле не перепишет. Transient раньше просто строил поле заново
                DurationEntry.Text = vm.DefaultDuration.ToString();
            });
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is SettingsViewModel vm) vm.PropertyChanged -= Vm_PropertyChanged;
        if (_subscribedWindow != null)
        {
            _subscribedWindow.Resumed -= OnWindowResumed;
            _subscribedWindow = null;
        }
    }

    /// <summary>
    /// Разрешения выдаются вне приложения — в системном диалоге и на системном
    /// экране, — и возврат оттуда не вызывает OnAppearing: страница никуда не
    /// уходила. Без этой подписки выданная точность будильника не доходила до
    /// экрана вовсе, и подсказка о задержке оставалась висеть навсегда.
    /// </summary>
    private void SubscribeToResume()
    {
        if (_subscribedWindow != null) _subscribedWindow.Resumed -= OnWindowResumed;
        _subscribedWindow = Window;
        if (_subscribedWindow != null) _subscribedWindow.Resumed += OnWindowResumed;
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        // Снаружи приложения меняются только разрешения — список расписаний и настройки
        // некому тронуть, — поэтому полное чтение здесь ни к чему
        if (BindingContext is SettingsViewModel vm)
            SafeFireAndForget.Run(vm.RefreshPermissionsAsync, nameof(OnWindowResumed));
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
