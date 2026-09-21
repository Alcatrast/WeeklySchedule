
namespace WeeklySchedule.Services;

public interface IFilePickerService
{
    Task<FileResult?> PickExcelFileAsync();
    Task<FileResult?> PickWscFileAsync();
    Task<bool> SaveFileAsync(string fileName, byte[] data);
}