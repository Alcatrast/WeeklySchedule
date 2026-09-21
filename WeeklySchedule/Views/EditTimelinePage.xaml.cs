using System.ComponentModel;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class EditTimelinePage : ContentPage
{
    private readonly EditTimelineViewModel _vm;
    private readonly IGroupSelectionPageFactory _groupPageFactory;

    public EditTimelinePage(
        EditTimelineViewModel vm,
        IGroupSelectionPageFactory groupPageFactory)
    {
        InitializeComponent();
        _vm = vm;
        _groupPageFactory = groupPageFactory;
        BindingContext = _vm;
        _vm.ImportRequested += OnImportRequested;
    }

    public void Initialize(Timeline? timeline)
    {
        _vm.Initialize(timeline);
    }

    private void OnImportRequested(string filePath, Timeline timeline, bool timelineExists)
    {
        SafeFireAndForget.Run(() => _groupPageFactory.OpenAsync(filePath, timelineExists, timeline, _vm.OnImportCompleted));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is EditTimelineViewModel vm)
        {
            vm.PropertyChanged -= Vm_PropertyChanged;
            vm.PropertyChanged += Vm_PropertyChanged;
            SafeFireAndForget.Run(vm.CheckPermissionsAsync);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm?.PropertyChanged -= Vm_PropertyChanged;
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not EditTimelineViewModel vm) return;

        if (e.PropertyName == nameof(EditTimelineViewModel.IsImporting))
        {
            SetImportButtonEnabled(!vm.IsImporting);
        }
        else if (e.PropertyName == nameof(EditTimelineViewModel.IsWscProcessing))
        {
            SetWscButtonsEnabled(!vm.IsWscProcessing);
        }
    }

    private void SetImportButtonEnabled(bool isEnabled)
    {
        ImportButton.InputTransparent = !isEnabled;
        ImportButton.Opacity = isEnabled ? 1.0 : 0.5;
    }

    private void SetWscButtonsEnabled(bool isEnabled)
    {
        ImportWscButton.InputTransparent = !isEnabled;
        ImportWscButton.Opacity = isEnabled ? 1.0 : 0.5;

        ExportWscButton.InputTransparent = !isEnabled;
        ExportWscButton.Opacity = isEnabled ? 1.0 : 0.5;
    }
}