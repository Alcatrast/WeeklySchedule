namespace WeeklySchedule.Data.Repositories;

public sealed class IncompleteLessonReadException(string path, Exception innerException)
    : IOException($"Не удалось прочитать сохранённую пару «{Path.GetFileName(path)}». " +
        "Импорт остановлен, прежние записи сохранены. Проверьте доступность и целостность файла.", innerException);
