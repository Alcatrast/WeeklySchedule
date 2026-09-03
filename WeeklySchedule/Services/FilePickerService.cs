using Microsoft.Maui.Storage;

namespace WeeklySchedule.Services;

public class FilePickerService : IFilePickerService
{
    public async Task<FileResult?> PickExcelFileAsync()
    {
        try
        {
            var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "application/vnd.ms-excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } },
                { DevicePlatform.WinUI, new[] { ".xls", ".xlsx" } }
            });

            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Выберите Excel-файл расписания",
                FileTypes = customFileType
            });

            if (result != null)
            {
                System.Diagnostics.Debug.WriteLine($"[FilePicker] Файл выбран: {result.FullPath}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[FilePicker] Пользователь отменил выбор файла.");
            }

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FilePicker] Ошибка выбора файла: {ex}");
            return null;
        }
    }
}