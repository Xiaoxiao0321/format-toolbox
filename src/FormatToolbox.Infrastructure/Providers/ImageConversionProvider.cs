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

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var validation = Validate(request);
        if (!validation.IsValid) return ConversionResult.Failure(validation.ErrorCode!, validation.Message!, sw.Elapsed, Id);
        string? temp = null;
        try
        {
            progress?.Report(new(10, "正在读取图片"));
            var output = OutputPathResolver.Resolve(request);
            OutputSafety.EnsureReady(output, new FileInfo(request.InputPath).Length * 2);
            temp = Path.Combine(Path.GetDirectoryName(output)!, $".{Guid.NewGuid():N}.tmp");
            await Task.Run(() => Encode(request, temp, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, output, request.OverwritePolicy == OverwritePolicy.Overwrite);
            progress?.Report(new(100, "转换完成"));
            return ConversionResult.Success(output, sw.Elapsed, Id);
        }
        catch (OperationCanceledException) { return ConversionResult.Failure(ErrorCodes.Cancelled, "任务已取消。", sw.Elapsed, Id); }
        catch (Exception ex) { return OutputSafety.Failure(ex, sw.Elapsed, Id); }
        finally { if (temp is not null && File.Exists(temp)) File.Delete(temp); }
    }

    private static void Encode(ConversionRequest request, string temp, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var input = File.OpenRead(request.InputPath);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapEncoder encoder = request.TargetFormat.TrimStart('.').ToLowerInvariant() switch
        {
            "png" => new PngBitmapEncoder(),
            "jpg" or "jpeg" => new JpegBitmapEncoder { QualityLevel = (request.Options as ImageOptions)?.Quality ?? 90 },
            "bmp" => new BmpBitmapEncoder(),
            "tif" or "tiff" => new TiffBitmapEncoder(),
            _ => throw new NotSupportedException("不支持目标图片格式。")
        };
        foreach (var frame in decoder.Frames) encoder.Frames.Add(frame);
        using var output = File.Create(temp);
        encoder.Save(output);
    }
}
