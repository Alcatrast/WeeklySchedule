using System.Text;

namespace WeeklySchedule.Utilities;

public static class AtomicFile
{
    // Временный файл в той же папке: до успешной замены оригинал не меняется.
    public static void WriteAllText(string path, string contents) =>
        Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            writer.Write(contents);
            writer.Flush();
        });

    /// <summary>
    /// То же для двоичных данных: сохраненный исходник расписания оборванной
    /// записью испортить нельзя, иначе повторный разбор читал бы обрубок.
    /// </summary>
    public static void WriteAllStream(string path, Stream contents) =>
        Write(path, contents.CopyTo);

    private static void Write(string path, Action<FileStream> fill)
    {
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fill(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(tempPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
