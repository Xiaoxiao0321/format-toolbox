using System.Diagnostics;
using FormatToolbox.Core;
using PDFtoImage;
using PDFtoImage.Exceptions;
using SkiaSharp;

namespace FormatToolbox.Infrastructure.Providers;

public sealed class PdfRenderProvider : IConversionProvider
{
    private static readonly HashSet<string> Inputs = new(StringComparer.OrdinalIgnoreCase) { "pdf" };
    private static readonly HashSet<string> Outputs = new(StringComparer.OrdinalIgnoreCase) { "png", "jpg", "jpeg" };
    public string Id => "pdf.pdfium-render";
    public ConversionCapability Capability => new(Id, Inputs, Outputs, "PDFium / SkiaSharp", true);
    public ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AvailabilityResult(true));
    public ValidationResult Validate(ConversionRequest request)
    {
        if (!File.Exists(request.InputPath)) return new(false, ErrorCodes.FileNotFound, "找不到 PDF 文件。");
        if (!Outputs.Contains(request.TargetFormat)) return new(false, ErrorCodes.UnsupportedFormat, "PDF 导图仅支持 PNG 和 JPEG。");
        var settings = request.Options as PdfRenderOptions ?? new PdfRenderOptions();
        if (settings.Dpi is < 72 or > 600) return new(false, ErrorCodes.UnsupportedFormat, "渲染 DPI 必须为 72–600。");
        if (!request.TargetFormat.Equals("png", StringComparison.OrdinalIgnoreCase) && settings.JpegQuality is < 1 or > 100)
            return new(false, ErrorCodes.UnsupportedFormat, "JPEG 质量必须为 1–100。");
        return ValidationResult.Valid;
    }

    public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
        => Task.Run(() => Render(request, progress, cancellationToken));

    private ConversionResult Render(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var validation = Validate(request);
        if (!validation.IsValid) return ConversionResult.Failure(validation.ErrorCode!, validation.Message!, sw.Elapsed, Id);
        var outputs = new List<string>(); var temporary = new List<string>();
        int? total = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var pdf = new FileStream(request.InputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
            var pageCount = Conversion.GetPageCount(pdf, leaveOpen: true);
            var settings = request.Options as PdfRenderOptions ?? new PdfRenderOptions();
            var pages = PageRanges.Parse(settings.PageRange, pageCount);
            total = pages.Count;
            var directory = request.OutputDirectory ?? Path.Combine(Path.GetDirectoryName(request.InputPath)!, "转换结果");
            OutputSafety.EnsureReady(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(request.InputPath)}-第1页.{request.TargetFormat}"), new FileInfo(request.InputPath).Length * 4);
            var format = request.TargetFormat.Equals("jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg" : request.TargetFormat.ToLowerInvariant();
            var renderOptions = new RenderOptions(Dpi: settings.Dpi);
            for (var i = 0; i < pages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var desired = ResolvePagePath(directory, Path.GetFileNameWithoutExtension(request.InputPath), pages[i] + 1, format);
                var temp = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp"); temporary.Add(temp);
                pdf.Position = 0;
                if (format == "png") Conversion.SavePng(temp, pdf, pages[i], leaveOpen: true, options: renderOptions);
                else
                {
                    using var bitmap = Conversion.ToImage(pdf, pages[i], leaveOpen: true, options: renderOptions);
                    using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, settings.JpegQuality);
                    using var stream = File.Create(temp);
                    encoded.SaveTo(stream);
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temp, desired); temporary.Remove(temp); outputs.Add(desired);
                progress?.Report(new(100d * (i + 1) / pages.Count, $"已渲染 {i + 1}/{pages.Count} 页"));
            }
            return new(ConversionStatus.Succeeded, outputs, [], null, null, sw.Elapsed, Id);
        }
        catch (OperationCanceledException) { return ConversionResult.Failure(ErrorCodes.Cancelled, "导图已取消。", sw.Elapsed, Id).WithCompletedOutputs(outputs, total); }
        catch (PdfPasswordProtectedException ex) { return ConversionResult.Failure(ErrorCodes.InvalidOrEncrypted, "PDF 已加密，需要密码才能渲染：" + ex.Message, sw.Elapsed, Id); }
        catch (PdfInvalidFormatException ex) { return ConversionResult.Failure(ErrorCodes.InvalidOrEncrypted, "PDF 文件已损坏或格式无效：" + ex.Message, sw.Elapsed, Id); }
        catch (Exception ex) { return OutputSafety.Failure(ex, sw.Elapsed, Id).WithCompletedOutputs(outputs, total); }
        finally { foreach (var file in temporary) TemporaryFileCleanup.DeleteFile(file, Id); }
    }

    private static string ResolvePagePath(string directory, string name, int page, string format)
    {
        var desired = Path.Combine(directory, $"{name}-第{page}页.{format}");
        for (var i = 1; File.Exists(desired); i++) desired = Path.Combine(directory, $"{name}-第{page}页 ({i}).{format}");
        return desired;
    }
}

internal static class PageRanges
{
    internal static IReadOnlyList<int> Parse(string? range, int pageCount)
    {
        if (string.IsNullOrWhiteSpace(range)) return Enumerable.Range(0, pageCount).ToArray();
        var pages = new SortedSet<int>();
        foreach (var segment in range.Split(',', StringSplitOptions.TrimEntries))
        {
            var bounds = segment.Split('-', StringSplitOptions.TrimEntries);
            if (bounds.Length > 2) throw new FormatException($"无效页码范围：{segment}");
            if (!int.TryParse(bounds[0], out var start) || start < 1 || start > pageCount) throw new FormatException($"无效页码：{segment}");
            var end = bounds.Length == 1 ? start : int.TryParse(bounds[1], out var parsed) ? parsed : throw new FormatException($"无效页码：{segment}");
            if (end < start || end > pageCount) throw new FormatException($"无效页码范围：{segment}");
            for (var page = start; page <= end; page++) pages.Add(page - 1);
        }
        if (pages.Count == 0) throw new FormatException("选定范围没有可输出的页面。");
        return pages.ToArray();
    }
}
