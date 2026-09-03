using System.Collections.ObjectModel;
using WeeklySchedule.ViewModels;

namespace WeeklySchedule.Models;

public class GroupCategory : BaseViewModel
{
    public string Prefix { get; set; } = string.Empty;
    public ObservableCollection<GroupItem> Groups { get; set; } = [];

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

public class GroupItem : BaseViewModel
{
    public string FullGroupName { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}