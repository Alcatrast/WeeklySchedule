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

    public ObservableCollection<Timeline> Timelines { get; } = [];
    public ICommand EditTimelineCommand { get; }
    public ICommand CreateTimelineCommand { get; }

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
        SafeFireAndForget.Run(LoadTimelinesAsync);
    }

    public async Task LoadTimelinesAsync()
    {
        _openLastTimeline = _settingsService.OpenLastTimeline;
        OnPropertyChanged(nameof(OpenLastTimeline));
        OnPropertyChanged(nameof(HighlightedTimelineId));
        Timelines.Clear();

        var all = await _repository.GetAllAsync();
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
        SafeFireAndForget.Run(async () =>
        {
            // Резолвим страницу через DI
            var editPage = _serviceProvider.GetRequiredService<Views.EditTimelinePage>();
            editPage.Initialize(timeline);
            await Shell.Current!.Navigation.PushModalAsync(editPage);
        });
    }
}