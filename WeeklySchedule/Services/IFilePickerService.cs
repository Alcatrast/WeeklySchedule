// WeeklySchedule/Services/IFilePickerService.cs
using Microsoft.Maui.Storage;

namespace WeeklySchedule.Services;

public interface IFilePickerService
{
    Task<FileResult?> PickExcelFileAsync();
}