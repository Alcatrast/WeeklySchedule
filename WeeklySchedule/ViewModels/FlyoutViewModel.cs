using System.Collections.ObjectModel;
using System.Windows.Input;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.ViewModels;

public partial class TimelineFlyoutItem(Timeline timeline) : BaseViewModel
{
    public Timeline Timeline { get; } = timeline;
    public Guid Id => Timeline.Id;
    public string Name => Timeline.Name;
    public bool NotificationsEnabled => Timeline.NotificationsEnabled;
    private bool _isActive;
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
    private bool _isHighlighted;
    public bool IsHighlighted { get => _isHighlighted; set => SetProperty(ref _isHighlighted, value); }
}

public partial class FlyoutViewModel : BaseViewModel, IDisposable
{
    private readonly ITimelineRepository _repository;
    private readonly IActiveScheduleService _scheduleService;
    private readonly ISettingsService _settingsService;
    private int _loadVersion;

    public ObservableCollection<TimelineFlyoutItem> Timelines { get; } = [];
    public ICommand SelectTimelineCommand { get; }

    public FlyoutViewModel(ITimelineRepository repository, IActiveScheduleService scheduleService, ISettingsService settingsService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _scheduleService = scheduleService ?? throw new ArgumentNullException(nameof(scheduleService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        SelectTimelineCommand = new Command<Timeline>(OnSelectTimeline);
        _scheduleService.ActiveTimelineChanged += OnActiveTimelineChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    private void OnActiveTimelineChanged(Guid _) => SafeFireAndForget.Run(LoadTimelinesAsync);
    private void OnSettingsChanged() => SafeFireAndForget.Run(LoadTimelinesAsync);

    public void Dispose()
    {
        _scheduleService.ActiveTimelineChanged -= OnActiveTimelineChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;
    }

    public async Task LoadTimelinesAsync()
    {
        var version = ++_loadVersion;
        var activeId = _scheduleService.ActiveTimelineId;
        var startupId = _settingsService.StartupTimelineId;
        bool isOpenLast = _settingsService.OpenLastTimeline;
        bool shouldHighlight = !isOpenLast && startupId != Guid.Empty;
        var allTimelines = (await _repository.GetAllAsync()).ToList();
        if (version != _loadVersion) return;
        Timelines.Clear();
        foreach (var t in allTimelines) Timelines.Add(new TimelineFlyoutItem(t) { IsActive = t.Id == activeId, IsHighlighted = shouldHighlight && t.Id == startupId });
    }

    private void OnSelectTimeline(Timeline? timeline) { if (timeline == null) return; Shell.Current!.FlyoutIsPresented = false; _scheduleService.ActiveTimelineId = timeline.Id; }
}