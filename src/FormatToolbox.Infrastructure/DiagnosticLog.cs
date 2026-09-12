using System.Text;

namespace FormatToolbox.Infrastructure;

public static class DiagnosticLog
{
    private static readonly object Sync = new();
    private const long MaxBytes = 5 * 1024 * 1024;
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FormatToolbox", "Logs");
    public static string FilePath => Path.Combine(DirectoryPath, "diagnostic.log");

    public static void Write(string area, Exception exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                {
                    var previous = Path.Combine(DirectoryPath, "diagnostic.previous.log");
                    File.Move(FilePath, previous, true);
                }
                var text = $"{DateTimeOffset.Now:O} [{area}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
                File.AppendAllText(FilePath, text, new UTF8Encoding(false));
            }
        }
        catch { }
    }
}
