using System.Windows.Input;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.Messaging;

namespace WeeklySchedule.ViewModels;

public partial class EditTimelineViewModel : BaseViewModel
{
    private readonly ITimelineRepository _repository;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly IFilePickerService _filePickerService;
    private readonly INavigationService _navigationService;
    private readonly Timeline _timeline;
    private readonly bool _isEditMode;
    private bool _isProcessing;

    public string ImportSectionTitle => _isEditMode ? "Дополнить импортом" : "Импорт";

    private bool _isImporting;
    public bool IsImporting
    {
        get => _isImporting;
        set => SetProperty(ref _isImporting, value);
    }

    public ICommand SelectExcelFileCommand { get; }
    public ICommand ToggleIsStartupCommand { get; }
    public string Title => _isEditMode ? "Редактирование таймлайна" : "Новый таймлайн";
    public bool IsEditMode => _isEditMode;

    private string _name;
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private bool _isStartupTimeline;
    public bool IsStartupTimeline { get => _isStartupTimeline; set => SetProperty(ref _isStartupTimeline, value); }

    private bool _notificationsEnabled;
    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set
        {
            if (SetProperty(ref _notificationsEnabled, value))
                _timeline.NotificationsEnabled = value;
        }
    }

    private bool _showPermissionWarning;
    public bool ShowPermissionWarning
    {
        get => _showPermissionWarning;
        set => SetProperty(ref _showPermissionWarning, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ToggleNotificationsCommand { get; }

    public EditTimelineViewModel(
        ITimelineRepository repository,
        ISettingsService settingsService,
        INotificationService notificationService,
        IFilePickerService filePickerService,
        INavigationService navigationService,
        Timeline? timeline)
    {
        _repository = repository;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _filePickerService = filePickerService;
        _navigationService = navigationService;

        _isEditMode = timeline != null;
        _timeline = timeline ?? new Timeline();
        _name = _timeline.Name;

        ToggleIsStartupCommand = new Command(() => IsStartupTimeline = !IsStartupTimeline);
        _isStartupTimeline = _settingsService.StartupTimelineId == _timeline.Id;

        SaveCommand = new Command(() => RunOperation(SaveAsync));
        DeleteCommand = new Command(() => RunOperation(DeleteAsync));
        CancelCommand = new Command(() => RunOperation(_navigationService.PopModalAsync));

        _notificationsEnabled = _timeline.NotificationsEnabled;
        ToggleNotificationsCommand = new Command(() => NotificationsEnabled = !NotificationsEnabled);
        SelectExcelFileCommand = new Command(() => RunOperation(HandleImportAsync));
    }

    private void RunOperation(Func<Task> operation) => SafeFireAndForget.Run(async () =>
    {
        if (_isProcessing) return;
        _isProcessing = true;
        try { await operation(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
                await page.DisplayAlertAsync("Ошибка", "Не удалось завершить операцию. Проверьте доступ к файлам и повторите попытку.", "ОК");
        }
        finally { _isProcessing = false; }
    });

    public async Task CheckPermissionsAsync()
    {
        var granted = await _notificationService.CheckAllPermissionsAsync();
        ShowPermissionWarning = !granted;
    }

    private async Task HandleImportAsync()
    {
        if (IsImporting) return;
        IsImporting = true;
        try
        {
            var file = await _filePickerService.PickExcelFileAsync();
            if (file == null) return;

            // Импорт должен попасть именно в редактируемый таймлайн, поэтому отдаем
            // сам объект: в режиме создания он еще не сохранен в репозитории
            _timeline.Name = (Name ?? string.Empty).Trim();

            // Передаем управление в View, так как создание страниц с DI лучше делать там
            // Или можно использовать IPageFactory. Для простоты вызываем событие.
            ImportRequested?.Invoke(file.FullPath, _timeline, _isEditMode);
        }
        finally
        {
            IsImporting = false;
        }
    }

    /// <summary>
    /// filePath, редактируемый таймлайн, признак того что таймлайн уже есть в репозитории.
    /// </summary>
    public event Action<string, Timeline, bool>? ImportRequested;

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            if (Application.Current?.Windows.FirstOrDefault()?.Page is Page page)
                await page.DisplayAlertAsync("Ошибка", "Введите название таймлайна", "ОК");
            return;
        }

        _timeline.Name = Name.Trim();
        if (_isEditMode) await _repository.UpdateAsync(_timeline);
        else await _repository.AddAsync(_timeline);

        ApplyStartupSelection();
        AppEvents.NotifyDataChanged();
        await _navigationService.PopModalAsync();
    }

    // Вызывается также после успешного импорта: тот закрывает редактор без SaveAsync.
    public void ApplyStartupSelection()
    {
        if (IsStartupTimeline)
        {
            _settingsService.StartupTimelineId = _timeline.Id;
            _settingsService.OpenLastTimeline = false;
        }
        else if (_settingsService.StartupTimelineId == _timeline.Id)
        {
            _settingsService.StartupTimelineId = Guid.Empty;
        }

    }

    private async Task DeleteAsync()
    {
        bool confirm = false;
        if (Application.Current?.Windows.FirstOrDefault()?.Page is Page page)
            confirm = await page.DisplayAlertAsync("Подтверждение", "Удалить этот таймлайн?", "Да", "Отмена");

        if (confirm)
        {
            if (_settingsService.StartupTimelineId == _timeline.Id)
                _settingsService.StartupTimelineId = Guid.Empty;

            await _repository.DeleteAsync(_timeline.Id);
            AppEvents.NotifyDataChanged();
            await _navigationService.PopModalAsync();
        }
    }
}
