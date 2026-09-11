
namespace WeeklySchedule.Services;

public interface IFilePickerService
{
    Task<FileResult?> PickExcelFileAsync();
}