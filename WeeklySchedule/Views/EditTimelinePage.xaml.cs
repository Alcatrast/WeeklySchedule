using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Views;

public partial class EditTimelinePage : ContentPage
{
    private EditTimelineViewModel? _vm;

    // Конструктор для DI
    public EditTimelinePage(
        ITimelineRepository repository,
        ISettingsService settingsService,
        INotificationService notificationService,
        IFilePickerService filePickerService,
        INavigationService navigationService)
    {
        InitializeComponent();

        _vm = new EditTimelineViewModel(repository, settingsService, notificationService, filePickerService, navigationService, null);
        BindingContext = _vm;
        _vm.ImportRequested += OnImportRequested;

        ImportButton.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb("#4361EE"), Offset = 0.0f }, // Можно вынести в Colors.xaml
                new GradientStop { Color = Color.FromArgb("#FF006E"), Offset = 1.0f }
            }
        };
    }

    public void Initialize(Timeline? timeline)
    {
        if (_vm != null)
        {
            // Пересоздаем VM с нужным таймлайном, так как он был null в конструкторе
            // В реальном проекте лучше использовать фабрику или передавать timeline в конструктор
            // Но для сохранения структуры оставим так
            var services = Application.Current!.Handler!.MauiContext!.Services;
            _vm = new EditTimelineViewModel(
                services.GetRequiredService<ITimelineRepository>(),
                services.GetRequiredService<ISettingsService>(),
                services.GetRequiredService<INotificationService>(),
                services.GetRequiredService<IFilePickerService>(),
                services.GetRequiredService<INavigationService>(),
                timeline);
            BindingContext = _vm;
            _vm.ImportRequested += OnImportRequested;
        }
    }

    private void OnImportRequested(string filePath, Timeline timeline, bool timelineExists)
    {
        SafeFireAndForget.Run(async () =>
        {
            var services = Application.Current!.Handler!.MauiContext!.Services;
            var groupPage = new GroupSelectionPage(
                filePath,
                timelineExists,
                timeline,
                services.GetRequiredService<ILessonRepository>(),
                services.GetRequiredService<ITimelineRepository>(),
                services.GetRequiredService<INavigationService>(),
                services);

            await Shell.Current!.Navigation.PushModalAsync(groupPage);
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is EditTimelineViewModel vm)
        {
            vm.PropertyChanged += Vm_PropertyChanged;
            SafeFireAndForget.Run(vm.CheckPermissionsAsync);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_vm != null)
        {
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.ImportRequested -= OnImportRequested;
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditTimelineViewModel.IsImporting) && sender is EditTimelineViewModel vm)
        {
            SetImportButtonEnabled(!vm.IsImporting);
        }
    }

    private void SetImportButtonEnabled(bool isEnabled)
    {
        ImportButton.InputTransparent = !isEnabled;
        ImportButton.Opacity = isEnabled ? 1.0 : 0.5;
    }
}