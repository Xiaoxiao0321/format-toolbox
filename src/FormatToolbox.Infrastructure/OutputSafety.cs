using FormatToolbox.Core;

namespace FormatToolbox.Infrastructure;

public sealed class ConversionIOException(string code, string message, Exception? inner = null) : IOException(message, inner)
{
    public string Code { get; } = code;
}

public static class OutputSafety
{
    public static void EnsureReady(string outputPath, long estimatedBytes)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? throw new ConversionIOException(ErrorCodes.AccessDenied, "无法确定输出目录。");
        try { Directory.CreateDirectory(directory); }
        catch (UnauthorizedAccessException ex) { throw new ConversionIOException(ErrorCodes.ReadOnlyOutput, "输出目录为只读或当前用户没有写入权限。", ex); }

        var root = Path.GetPathRoot(directory);
        if (!string.IsNullOrWhiteSpace(root))
        {
            var drive = new DriveInfo(root);
            var required = Math.Max(64L * 1024 * 1024, estimatedBytes);
            if (drive.IsReady && drive.AvailableFreeSpace < required)
                throw new ConversionIOException(ErrorCodes.DiskFull, $"输出磁盘空间不足。至少需要约 {required / 1024 / 1024} MB 可用空间。 ");
        }

        var probe = Path.Combine(directory, $".format-toolbox-write-test-{Guid.NewGuid():N}.tmp");
        try { using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose); stream.WriteByte(0); }
        catch (UnauthorizedAccessException ex) { throw new ConversionIOException(ErrorCodes.ReadOnlyOutput, "输出目录为只读或当前用户没有写入权限。", ex); }
        catch (IOException ex) { throw FromIOException(ex, "无法写入输出目录。"); }
        finally { try { if (File.Exists(probe)) File.Delete(probe); } catch { } }

        if (File.Exists(outputPath))
        {
            try { using var stream = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) { throw new ConversionIOException(ErrorCodes.OutputLocked, "输出文件正被其他程序占用，请关闭后重试。", ex); }
            catch (UnauthorizedAccessException ex) { throw new ConversionIOException(ErrorCodes.ReadOnlyOutput, "输出文件为只读或没有覆盖权限。", ex); }
        }
    }

    public static ConversionResult Failure(Exception exception, TimeSpan elapsed, string engine)
    {
        DiagnosticLog.Write(engine, exception);
        var identified = exception as ConversionIOException ?? (exception is IOException io ? FromIOException(io, io.Message) : null);
        return identified is not null
            ? ConversionResult.Failure(identified.Code, identified.Message, elapsed, engine)
            : exception is UnauthorizedAccessException
                ? ConversionResult.Failure(ErrorCodes.AccessDenied, "没有读取输入文件或写入输出目录的权限。", elapsed, engine)
                : ConversionResult.Failure(ErrorCodes.EngineFailure, exception.Message, elapsed, engine);
    }

    private static ConversionIOException FromIOException(IOException exception, string fallback)
    {
        var win32 = exception.HResult & 0xFFFF;
        return win32 switch
        {
            0x20 or 0x21 => new(ErrorCodes.OutputLocked, "文件正被其他程序占用，请关闭后重试。", exception),
            0x27 or 0x70 => new(ErrorCodes.DiskFull, "磁盘空间不足，请释放空间或更换输出目录。", exception),
            0x13 => new(ErrorCodes.ReadOnlyOutput, "输出介质或目录为只读。", exception),
            _ => new(ErrorCodes.EngineFailure, fallback, exception)
        };
    }
}
