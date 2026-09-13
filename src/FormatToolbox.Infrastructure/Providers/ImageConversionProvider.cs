using System.Diagnostics;
using System.Windows.Media.Imaging;
using FormatToolbox.Core;

namespace FormatToolbox.Infrastructure.Providers;

public sealed class ImageConversionProvider : IConversionProvider
{
    private static readonly HashSet<string> Inputs = new(StringComparer.OrdinalIgnoreCase) { "png", "jpg", "jpeg", "bmp", "tif", "tiff", "webp" };
    private static readonly HashSet<string> Outputs = new(StringComparer.OrdinalIgnoreCase) { "png", "jpg", "jpeg", "bmp", "tif", "tiff" };
    public string Id => "image.wic";
    public ConversionCapability Capability => new(Id, Inputs, Outputs, "Windows Imaging Component", true);
    public ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AvailabilityResult(true, Environment.OSVersion.VersionString));
    public ValidationResult Validate(ConversionRequest request) => File.Exists(request.InputPath) ? ValidationResult.Valid : new(false, ErrorCodes.FileNotFound, "找不到输入文件。");

    public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
        => Task.Run(() => Convert(request, progress, cancellationToken));

    private ConversionResult Convert(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var validation = Validate(request);
        if (!validation.IsValid) return ConversionResult.Failure(validation.ErrorCode!, validation.Message!, sw.Elapsed, Id);
        var outputs = new List<string>(); var temporary = new List<string>();
        int? total = null;
        try
        {
            progress?.Report(new(10, "正在读取图片"));
            cancellationToken.ThrowIfCancellationRequested();
            var frames = ImageFrameReader.Read(request.InputPath);
            var multiPageOutput = request.TargetFormat.TrimStart('.').ToLowerInvariant() is "tif" or "tiff";
            total = multiPageOutput ? 1 : frames.Count;
            for (var i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pageRequest = total > 1 ? request with { OutputFileName = (request.OutputFileName ?? Path.GetFileNameWithoutExtension(request.InputPath)) + $"-第{i + 1}页" } : request;
                var output = OutputPathResolver.Resolve(pageRequest);
                OutputSafety.EnsureReady(output, new FileInfo(request.InputPath).Length * 2);
                var temp = Path.Combine(Path.GetDirectoryName(output)!, $".{Guid.NewGuid():N}.tmp"); temporary.Add(temp);
                Encode(request, temp, multiPageOutput ? frames : [frames[i]], cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temp, output, request.OverwritePolicy == OverwritePolicy.Overwrite);
                temporary.Remove(temp); outputs.Add(output);
                progress?.Report(new(10 + 90d * outputs.Count / total.Value, $"已转换 {outputs.Count}/{total} 个文件"));
            }
            return new(ConversionStatus.Succeeded, outputs, [], null, null, sw.Elapsed, Id);
        }
        catch (OperationCanceledException) { return ConversionResult.Failure(ErrorCodes.Cancelled, "任务已取消。", sw.Elapsed, Id).WithCompletedOutputs(outputs, total); }
        catch (NotSupportedException ex) { return ConversionResult.Failure(ErrorCodes.UnsupportedFormat, ex.Message, sw.Elapsed, Id).WithCompletedOutputs(outputs, total); }
        catch (Exception ex) { return OutputSafety.Failure(ex, sw.Elapsed, Id).WithCompletedOutputs(outputs, total); }
        finally { foreach (var temp in temporary) TemporaryFileCleanup.DeleteFile(temp, Id); }
    }

    private static void Encode(ConversionRequest request, string temp, IReadOnlyList<BitmapFrame> frames, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        BitmapEncoder encoder = request.TargetFormat.TrimStart('.').ToLowerInvariant() switch
        {
            "png" => new PngBitmapEncoder(),
            "jpg" or "jpeg" => new JpegBitmapEncoder { QualityLevel = (request.Options as ImageOptions)?.Quality ?? 90 },
            "bmp" => new BmpBitmapEncoder(),
            "tif" or "tiff" => new TiffBitmapEncoder(),
            _ => throw new NotSupportedException("不支持目标图片格式。")
        };
        foreach (var frame in frames) { token.ThrowIfCancellationRequested(); encoder.Frames.Add(frame); }
        using var output = File.Create(temp);
        encoder.Save(output);
    }
}
