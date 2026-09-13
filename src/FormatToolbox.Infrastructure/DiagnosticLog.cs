using System.Text;

namespace FormatToolbox.Infrastructure;

public static class DiagnosticLog
{
    private static readonly object Sync = new();
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly Queue<string> RecentErrors = new();
    private static readonly Dictionary<string, string> Engines = new();
    public static void RecordEngine(string id, FormatToolbox.Core.AvailabilityResult status)
    {
        var line = $"引擎 {id}：{(status.IsAvailable ? "可用" : "不可用")}";
        lock (Sync) Engines[id] = line;
        Append(line);
    }
    public static string GetInformation()
    {
        var environment = DiagnosticInformation.Capture();
        lock (Sync) return environment + string.Join(Environment.NewLine, Engines.Values) + Environment.NewLine + "近期错误码（本次运行）：" + Environment.NewLine +
            (RecentErrors.Count == 0 ? "无" : string.Join(Environment.NewLine, RecentErrors)) + Environment.NewLine;
    }

    public static void WriteFailure(string area, string code, string? message, int? hresult = null)
    {
        var summary = $"{DateTimeOffset.Now:O} [{area}] ErrorCode={code}" + (hresult is null ? "" : $" HRESULT=0x{hresult.Value:X8}");
        lock (Sync) { RecentErrors.Enqueue(summary); while (RecentErrors.Count > 20) RecentErrors.Dequeue(); }
        Append(summary + Environment.NewLine + message + Environment.NewLine + DiagnosticInformation.Capture());
    }

    public static void WriteEnvironment() => Append(DiagnosticInformation.Capture());
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FormatToolbox", "Logs");
    public static string FilePath => Path.Combine(DirectoryPath, "diagnostic.log");

    public static void Write(string area, Exception exception)
        => WriteFailure(area, "EXCEPTION", exception.ToString(), exception.HResult);

    private static void Append(string details)
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
                var text = $"{DateTimeOffset.Now:O}{Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}";
                File.AppendAllText(FilePath, text, new UTF8Encoding(false));
            }
        }
        catch { }
    }
}
