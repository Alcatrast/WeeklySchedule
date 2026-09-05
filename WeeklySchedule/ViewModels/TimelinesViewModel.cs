using System.Collections.ObjectModel;
using System.Windows.Input;
using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.ViewModels;

public partial class TimelinesViewModel : BaseViewModel
{
    private readonly ITimelineRepository _repository;
    private readonly ISettingsService _settingsService;
    private readonly IServiceProvider _serviceProvider;
    private int _loadVersion;

    public ObservableCollection<Timeline> Timelines { get; } = [];
    public ICommand EditTimelineCommand { get; }
    public ICommand CreateTimelineCommand { get; }
    public ICommand TimelineActionsCommand { get; }

    private bool _openLastTimeline;
    public bool OpenLastTimeline
    {
        get => _openLastTimeline;
        set
        {
            if (SetProperty(ref _openLastTimeline, value))
            {
                _settingsService.OpenLastTimeline = value;
                OnPropertyChanged(nameof(HighlightedTimelineId));
            }
        }
    }

    public Guid HighlightedTimelineId => OpenLastTimeline ? Guid.Empty : _settingsService.StartupTimelineId;

    public TimelinesViewModel(ITimelineRepository repository, ISettingsService settingsService, IServiceProvider serviceProvider)
    {
        _repository = repository;
        _settingsService = settingsService;
        _serviceProvider = serviceProvider;

        EditTimelineCommand = new Command<Timeline>(OnEditTimeline);
        CreateTimelineCommand = new Command(OnCreateTimeline);
        TimelineActionsCommand = new Command<Timeline>(timeline =>
        {
            if (timeline != null) SafeFireAndForget.Run(() => ItemActions.ShowTimelineAsync(timeline));
        });
    }

    public async Task LoadTimelinesAsync()
    {
        var version = ++_loadVersion;
        var all = (await _repository.GetAllAsync()).ToList();
        if (version != _loadVersion) return;

        _openLastTimeline = _settingsService.OpenLastTimeline;
        OnPropertyChanged(nameof(OpenLastTimeline));
        OnPropertyChanged(nameof(HighlightedTimelineId));
        if (Timelines.Select(t => (t.Id, t.Name, t.NotificationsEnabled))
            .SequenceEqual(all.Select(t => (t.Id, t.Name, t.NotificationsEnabled))))
        {
            for (int i = 0; i < all.Count; i++) Timelines[i].BaseDays = all[i].BaseDays ?? [];
            return;
        }
        Timelines.Clear();

        foreach (var timeline in all) Timelines.Add(timeline);
    }

    private void OnEditTimeline(Timeline? timeline)
    {
        if (timeline == null) return;
        OpenEditPage(timeline);
    }

    private void OnCreateTimeline() => OpenEditPage(null);

    private void OpenEditPage(Timeline? timeline)
    {
        SafeFireAndForget.Run(() => ItemActions.OpenTimelineAsync(timeline));
    }
}
