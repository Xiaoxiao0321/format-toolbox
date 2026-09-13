namespace FormatToolbox.Infrastructure;

public static class TemporaryFileCleanup
{
    public static void DeleteFile(string path, string engine) => DeleteFileAsync(path, engine).GetAwaiter().GetResult();
    public static Task DeleteFileAsync(string path, string engine) => RetryAsync(() => File.Delete(path), engine);
    public static Task DeleteDirectoryAsync(string path, string engine) => RetryAsync(() => { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }, engine);

    private static async Task RetryAsync(Action delete, string engine)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { delete(); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 6) { DiagnosticLog.Write(engine + ".cleanup", ex); return; }
                await Task.Delay(25 << attempt).ConfigureAwait(false);
            }
        }
    }
}
