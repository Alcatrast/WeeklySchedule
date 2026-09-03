using WeeklySchedule.Models;

namespace WeeklySchedule.Converters;

public class ScheduleItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? LessonTemplate { get; set; }
    public DataTemplate? SeparatorTemplate { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container)
    {
        return item switch
        {
            LessonItem => LessonTemplate,
            SeparatorItem => SeparatorTemplate,
            _ => null
        };
    }
}