using CommunityToolkit.Maui.Storage;
using WeeklySchedule.Resources.Strings;

namespace WeeklySchedule.Services;

public class FilePickerService : IFilePickerService
{
    private static readonly string[] _excelAndroidTypes = [ "application/vnd.ms-excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" ];
    private static readonly string[] _excelWinUITypes = [ ".xls", ".xlsx" ];

    private static readonly string[] _wscAndroidTypes = [ "application/json", "application/octet-stream", "*/*" ];
    private static readonly string[] _wscWinUITypes = [ ".wsc" ];

    public async Task<FileResult?> PickExcelFileAsync()
    {
        try
        {
            var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, _excelAndroidTypes },
                { DevicePlatform.WinUI, _excelWinUITypes }
            });

            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = AppResources.PickExcel,
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

    public async Task<FileResult?> PickWscFileAsync()
    {
        try
        {
            var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, _wscAndroidTypes },
                { DevicePlatform.WinUI, _wscWinUITypes }
            });

            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = AppResources.PickWsc,
                FileTypes = customFileType
            });

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FilePicker] Ошибка выбора .wsc файла: {ex}");
            return null;
        }
    }

    public async Task<bool> SaveFileAsync(string fileName, byte[] data)
    {
        using var stream = new MemoryStream(data);
        var result = await FileSaver.Default.SaveAsync(fileName, stream);
        return result.IsSuccessful;
    }
}