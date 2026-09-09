using WeeklySchedule.Data.Repositories;
using WeeklySchedule.Models;

namespace WeeklySchedule.ViewModels;

/// <param name="timelineHint">
/// Расписание, в котором пара лежала, когда экран открывали. Позволяет прочитать один
/// файл вместо обхода всего хранилища. Пустой или промахнувшийся — не ошибка: пару
/// могли перенести редактором, и тогда идет прежний полный поиск.
/// </param>
public sealed class LessonDetailsViewModel(Guid lessonId, ILessonRepository lessons,
    ITimelineRepository timelines, Guid timelineHint = default)
{
    private int _version;
    public Lesson? Lesson { get; private set; }
    public string TimelineName { get; private set; } = "";
    public bool IsDeleted { get; private set; }

    public void CancelPendingRefresh() => ++_version;

    public async Task RefreshAsync()
    {
        var version = ++_version;
        var lesson = timelineHint == Guid.Empty ? null : await lessons.GetByIdAsync(timelineHint, lessonId);
        if (version != _version) return;
        lesson ??= await lessons.GetByIdAsync(lessonId);
        if (version != _version) return;
        var timeline = lesson == null ? null : await timelines.GetByIdAsync(lesson.TimelineId);
        if (version != _version) return;
        Lesson = lesson;
        IsDeleted = lesson == null;
        TimelineName = timeline?.Name ?? "Таймлайн удалён";
    }
}
