namespace WeeklySchedule.Models;

/// <summary>
/// Откуда таймлайн импортирован. Нужен, чтобы разбор можно было повторить: сам файл
/// лежит рядом с парами таймлайна, а здесь — чем именно его читали. Свойство на
/// Timeline nullable, поэтому старые timelines.json читаются без изменений; у них
/// исходника нет, и кнопка повторного разбора не появляется.
/// </summary>
public sealed class ImportSource
{
    /// <summary>Исходное имя файла — только для показа пользователю.</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>Имя собственной копии. У старых записей null означает source.xlsx.</summary>
    public string? StoredFileName { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; }
}
