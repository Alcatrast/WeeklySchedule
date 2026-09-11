using System.ComponentModel;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class EditTimelinePage : ContentPage
{
    private EditTimelineViewModel _vm;
    private readonly IGroupSelectionPageFactory _groupPageFactory;

    public EditTimelinePage(
        ViewModels.EditTimelineViewModel vm,
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
        if (_vm != null) _vm.ImportRequested -= OnImportRequested;
    }

    private void OnImportRequested(string filePath, Timeline timeline, bool timelineExists)
    {
        SafeFireAndForget.Run(() => _groupPageFactory.OpenAsync(filePath, timelineExists, timeline, _vm.ApplyStartupSelection));
    }

    protected override void OnAppearing() { base.OnAppearing(); if (BindingContext is EditTimelineViewModel vm) { vm.PropertyChanged -= Vm_PropertyChanged; vm.PropertyChanged += Vm_PropertyChanged; SafeFireAndForget.Run(vm.CheckPermissionsAsync); } }
    protected override void OnDisappearing() { base.OnDisappearing(); if (_vm != null) _vm.PropertyChanged -= Vm_PropertyChanged; }
    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(EditTimelineViewModel.IsImporting) && sender is EditTimelineViewModel vm) SetImportButtonEnabled(!vm.IsImporting); }
    private void SetImportButtonEnabled(bool isEnabled) { ImportButton.InputTransparent = !isEnabled; ImportButton.Opacity = isEnabled ? 1.0 : 0.5; }
}