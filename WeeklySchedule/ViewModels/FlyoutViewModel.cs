using System.Collections.ObjectModel;
using System.Windows.Input;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.Messaging;

namespace WeeklySchedule.ViewModels;

public partial class TimelineFlyoutItem(Timeline timeline) : BaseViewModel
{
    public Timeline Timeline { get; } = timeline;
    public Guid Id => Timeline.Id;
    public string Name => Timeline.Name;
    public bool NotificationsEnabled => Timeline.NotificationsEnabled;
    public void Update(Timeline timeline)
    {
        Timeline.Name = timeline.Name;
        Timeline.NotificationsEnabled = timeline.NotificationsEnabled;
        Timeline.BaseDays = timeline.BaseDays ?? [];
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(NotificationsEnabled));
    }

    private bool _isActive;
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    private bool _isHighlighted;
    public bool IsHighlighted { get => _isHighlighted; set => SetProperty(ref _isHighlighted, value); }
}

public partial class FlyoutViewModel : BaseViewModel
{
    private readonly ITimelineRepository _repository;
    private readonly IActiveScheduleService _scheduleService;
    private readonly ISettingsService _settingsService;
    private int _loadVersion;
    private int _dataRevision;
    private int _loadedRevision = -1;
    private Task? _refresh;

    public ObservableCollection<TimelineFlyoutItem> Timelines { get; } = [];
    public ICommand SelectTimelineCommand { get; }
    public ICommand TimelineActionsCommand { get; }

    public FlyoutViewModel(ITimelineRepository repository, IActiveScheduleService scheduleService, ISettingsService settingsService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _scheduleService = scheduleService ?? throw new ArgumentNullException(nameof(scheduleService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        SelectTimelineCommand = new Command<Timeline>(OnSelectTimeline);
        TimelineActionsCommand = new Command<Timeline>(timeline =>
        {
            if (timeline != null) SafeFireAndForget.Run(() => ItemActions.ShowTimelineAsync(timeline));
        });

        // События объявлены как Action/Action<T>, поэтому Task-метод приходится
        // запускать без ожидания. Раньше это была async void лямбда: исключение
        // из нее не ловилось нигде и роняло процесс
        _scheduleService.ActiveTimelineChanged += (_) => UpdateFlags();
        _settingsService.SettingsChanged += UpdateFlags;
        AppEvents.DataChanged += (_) =>
        {
            ++_dataRevision;
            SafeFireAndForget.Run(RefreshIfNeededAsync);
        };
    }

    private void UpdateFlags()
    {
        var activeId = _scheduleService.ActiveTimelineId;
        var startupId = _settingsService.StartupTimelineId;
        bool highlight = !_settingsService.OpenLastTimeline;
        foreach (var item in Timelines)
        {
            item.IsActive = item.Id == activeId;
            item.IsHighlighted = highlight && item.Id == startupId;
        }
    }

    public async Task RefreshIfNeededAsync()
    {
        if (_refresh != null) await _refresh;
        if (_loadedRevision == _dataRevision) { UpdateFlags(); return; }
        var task = LoadTimelinesAsync();
        _refresh = task;
        try { await task; }
        finally { if (ReferenceEquals(_refresh, task)) _refresh = null; }
        if (_loadedRevision != _dataRevision) await RefreshIfNeededAsync();
    }

    public async Task LoadTimelinesAsync()
    {
        var version = ++_loadVersion;
        var revision = _dataRevision;
        var all = (await _repository.GetAllAsync()).ToList();
        if (version != _loadVersion || revision != _dataRevision) return;
        var ids = all.Select(t => t.Id).ToHashSet();
        for (int i = Timelines.Count - 1; i >= 0; i--)
            if (!ids.Contains(Timelines[i].Id)) Timelines.RemoveAt(i);
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i];
            var item = Timelines.FirstOrDefault(existing => existing.Id == t.Id);
            if (item == null) { item = new TimelineFlyoutItem(t); Timelines.Insert(i, item); }
            else
            {
                item.Update(t);
                int oldIndex = Timelines.IndexOf(item);
                if (oldIndex != i) Timelines.Move(oldIndex, i);
            }
        }
        _loadedRevision = revision;
        UpdateFlags();
    }

    private void OnSelectTimeline(Timeline? timeline)
    {
        if (timeline == null) return;
        Shell.Current!.FlyoutIsPresented = false;
        _scheduleService.ActiveTimelineId = timeline.Id;
    }
}
