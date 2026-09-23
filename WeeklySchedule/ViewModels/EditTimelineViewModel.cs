using System.Text;
using System.Text.Json;
using System.Windows.Input;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Messaging;
using WeeklySchedule.Models;
using WeeklySchedule.Resources.Strings;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.ViewModels;

public partial class EditTimelineViewModel : BaseViewModel
{
    private readonly ITimelineRepository _repository;
    private readonly ILessonRepository _lessonRepo;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly IFilePickerService _filePickerService;
    private readonly INavigationService _navigationService;

    private Timeline _timeline = null!;
    private bool _isProcessing;
    private bool _isWscProcessing;

    private static readonly JsonSerializerOptions _exportJsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions _importJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private bool _isEditMode;
    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (SetProperty(ref _isEditMode, value))
            {
                OnPropertyChanged(nameof(ImportSectionTitle));
            }
        }
    }

    public string ImportSectionTitle => IsEditMode ? AppResources.ImportSectionEdit : AppResources.ImportSectionNew;

    private bool _isImporting;
    public bool IsImporting
    {
        get => _isImporting;
        set => SetProperty(ref _isImporting, value);
    }

    public bool IsWscProcessing
    {
        get => _isWscProcessing;
        set => SetProperty(ref _isWscProcessing, value);
    }

    public ICommand SelectExcelFileCommand { get; }
    public ICommand ToggleIsStartupCommand { get; }
    public ICommand ExportWscCommand { get; }
    public ICommand ImportWscCommand { get; }

    private string _title = AppResources.NewTimeline;
    public string Title { get => _title; set => SetProperty(ref _title, value); }

    private string _name = string.Empty;
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
            {
                _timeline.NotificationsEnabled = value;
            }
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
        ILessonRepository lessonRepo,
        ISettingsService settingsService,
        INotificationService notificationService,
        IFilePickerService filePickerService,
        INavigationService navigationService)
    {
        _repository = repository;
        _lessonRepo = lessonRepo;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _filePickerService = filePickerService;
        _navigationService = navigationService;

        ToggleIsStartupCommand = new Command(() => IsStartupTimeline = !IsStartupTimeline);
        SaveCommand = new Command(() => RunOperation(SaveAsync));
        DeleteCommand = new Command(() => RunOperation(DeleteAsync));
        CancelCommand = new Command(() => RunOperation(_navigationService.PopModalAsync));
        ToggleNotificationsCommand = new Command(() => NotificationsEnabled = !NotificationsEnabled);

        SelectExcelFileCommand = new Command(() => RunOperation(HandleImportAsync));
        ExportWscCommand = new Command(() => RunOperation(ExportWscAsync));
        ImportWscCommand = new Command(() => RunOperation(ImportWscAsync));
    }

    public void Initialize(Timeline? timeline)
    {
        _timeline = timeline ?? new Timeline();
        _name = _timeline.Name;
        IsEditMode = timeline != null;
        _title = IsEditMode ? AppResources.EditTimeline : AppResources.NewTimeline;
        _isStartupTimeline = _settingsService.StartupTimelineId == _timeline.Id;
        _notificationsEnabled = _timeline.NotificationsEnabled;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsStartupTimeline));
        OnPropertyChanged(nameof(NotificationsEnabled));
    }

    private void RunOperation(Func<Task> operation) => SafeFireAndForget.Run(async () =>
    {
        if (_isProcessing || _isWscProcessing) return;
        _isProcessing = true;
        try { await operation(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (Application.Current?.Windows[0]?.Page is Page page)
                await page.DisplayAlertAsync(AppResources.Error, string.Format(AppResources.OperationError, ex.Message), AppResources.OK);
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
            _timeline.Name = (Name ?? string.Empty).Trim();
            ImportRequested?.Invoke(file.FullPath, _timeline, IsEditMode);
        }
        finally
        {
            IsImporting = false;
        }
    }

    public event Action<string, Timeline, bool>? ImportRequested;

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            if (Application.Current?.Windows[0]?.Page is Page page)
                await page.DisplayAlertAsync(AppResources.Error, AppResources.EnterTimelineName, AppResources.OK);
            return;
        }

        _timeline.Name = Name.Trim();

        if (IsEditMode)
            await _repository.UpdateAsync(_timeline);
        else
            await _repository.AddAsync(_timeline);

        ApplyStartupSelection();
        AppEvents.NotifyDataChanged();
        await _navigationService.PopModalAsync();
    }

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

    public void OnImportCompleted()
    {
        if (!IsEditMode)
        {
            SafeFireAndForget.Run(async () =>
            {
                await _repository.AddAsync(_timeline);
                IsEditMode = true;
                Name = _timeline.Name;
                Title = AppResources.EditTimeline;
                ApplyStartupSelection();
                AppEvents.NotifyDataChanged();
            });
        }
        else
        {
            ApplyStartupSelection();
        }
    }

    private async Task DeleteAsync()
    {
        bool confirm = false;
        if (Application.Current?.Windows[0]?.Page is Page page)
            confirm = await page.DisplayAlertAsync(AppResources.ConfirmDelete, AppResources.ConfirmDeleteTimeline, AppResources.Yes, AppResources.Cancel);

        if (confirm)
        {
            if (_settingsService.StartupTimelineId == _timeline.Id)
                _settingsService.StartupTimelineId = Guid.Empty;

            await _repository.DeleteAsync(_timeline.Id);
            AppEvents.NotifyDataChanged();
            await _navigationService.PopModalAsync();
        }
    }

    private async Task ExportWscAsync()
    {
        if (IsWscProcessing) return;
        IsWscProcessing = true;
        try
        {
            var lessons = (await _lessonRepo.GetByTimelineIdAsync(_timeline.Id)).ToList();
            var exportData = new WscExportData
            {
                TimelineName = (Name ?? string.Empty).Trim(),
                Lessons = lessons
            };
            var json = JsonSerializer.Serialize(exportData, _exportJsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            var fileName = string.IsNullOrWhiteSpace(exportData.TimelineName) ? "timeline" : exportData.TimelineName;
            fileName = string.Concat(fileName.Split(Path.GetInvalidFileNameChars())) + ".wsc";

            var success = await _filePickerService.SaveFileAsync(fileName, bytes);
            if (success)
            {
                await ShowPageAlertAsync(AppResources.ExportSuccessTitle, string.Format(AppResources.ExportSuccessMsg, exportData.TimelineName, lessons.Count));
            }
        }
        catch (Exception ex)
        {
            await ShowPageAlertAsync(AppResources.Error, string.Format(AppResources.ExportError, ex.Message));
        }
        finally
        {
            IsWscProcessing = false;
        }
    }

    private async Task ImportWscAsync()
    {
        if (IsWscProcessing) return;
        IsWscProcessing = true;
        try
        {
            var file = await _filePickerService.PickWscFileAsync();
            if (file == null) return;

            if (!file.FileName.EndsWith(".wsc", StringComparison.OrdinalIgnoreCase))
            {
                await ShowPageAlertAsync(AppResources.Error, AppResources.NotWscError);
                return;
            }

            using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var json = await reader.ReadToEndAsync();
            var importData = JsonSerializer.Deserialize<WscExportData>(json, _importJsonOptions);

            if (importData == null || importData.Lessons == null)
            {
                await ShowPageAlertAsync(AppResources.Error, AppResources.ReadError);
                return;
            }

            if (IsEditMode)
            {
                foreach (var lesson in importData.Lessons)
                {
                    var newLesson = new Lesson
                    {
                        TimelineId = _timeline.Id,
                        Name = lesson.Name,
                        Description = lesson.Description,
                        StartTime = lesson.StartTime,
                        EndTime = lesson.EndTime,
                        Type = lesson.Type,
                        Day = lesson.Day
                    };
                    await _lessonRepo.AddAsync(newLesson);
                }
            }
            else
            {
                var timelineName = Path.GetFileNameWithoutExtension(file.FileName);
                _timeline.Name = timelineName;
                await _repository.AddAsync(_timeline);
                IsEditMode = true;
                Name = timelineName;
                Title = AppResources.EditTimeline;

                foreach (var lesson in importData.Lessons)
                {
                    var newLesson = new Lesson
                    {
                        TimelineId = _timeline.Id,
                        Name = lesson.Name,
                        Description = lesson.Description,
                        StartTime = lesson.StartTime,
                        EndTime = lesson.EndTime,
                        Type = lesson.Type,
                        Day = lesson.Day
                    };
                    await _lessonRepo.AddAsync(newLesson);
                }
            }

            ApplyStartupSelection();
            AppEvents.NotifyDataChanged();
            await ShowPageAlertAsync(AppResources.ImportSuccessTitle, string.Format(AppResources.ImportSuccessMsg, importData.Lessons.Count));
        }
        catch (Exception ex)
        {
            await ShowPageAlertAsync(AppResources.Error, string.Format(AppResources.ImportError, ex.Message));
        }
        finally
        {
            IsWscProcessing = false;
        }
    }

    private static Task ShowPageAlertAsync(string title, string message)
    {
        var windows = Application.Current?.Windows;
        var page = (windows != null && windows.Count > 0) ? windows[0].Page : null;
        return page?.DisplayAlertAsync(title, message, AppResources.OK) ?? Task.CompletedTask;
    }
}