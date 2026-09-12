using System.Diagnostics;
using FormatToolbox.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFtoImage;
using SkiaSharp;

namespace FormatToolbox.Infrastructure.Providers;

public sealed class PdfConversionProvider : IConversionProvider
{
    private static readonly HashSet<string> Inputs = new(StringComparer.OrdinalIgnoreCase) { "pdf", "png", "jpg", "jpeg", "bmp", "tif", "tiff" };
    private static readonly HashSet<string> Outputs = new(StringComparer.OrdinalIgnoreCase) { "pdf" };
    public string Id => "pdf.pdfsharp";
    public ConversionCapability Capability => new(Id, Inputs, Outputs, "PDFsharp 6.2.4 (MIT)", true);
    public ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AvailabilityResult(true, "6.2.4"));

    public ValidationResult Validate(ConversionRequest request)
    {
        if (!File.Exists(request.InputPath)) return new(false, ErrorCodes.FileNotFound, "找不到输入文件。");
        if (!request.TargetFormat.Equals("pdf", StringComparison.OrdinalIgnoreCase)) return new(false, ErrorCodes.UnsupportedFormat, "PDF 引擎只输出 PDF。 ");
        var options = request.Options as PdfOptions;
        if (options?.RotationDegrees is { } rotation && rotation % 90 != 0) return new(false, ErrorCodes.UnsupportedFormat, "旋转角度必须是 90 的倍数。");
        if (options?.RasterizeForCompression == true && (options.CompressionDpi is < 72 or > 300 || options.CompressionJpegQuality is < 20 or > 95)) return new(false, ErrorCodes.UnsupportedFormat, "强力压缩 DPI 必须为 72–300，JPEG 质量必须为 20–95。 ");
        if (options?.AdditionalInputs?.Any(path => !File.Exists(path)) == true) return new(false, ErrorCodes.FileNotFound, "合并列表中存在找不到的文件。");
        return ValidationResult.Valid;
    }

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var validation = Validate(request);
        if (!validation.IsValid) return ConversionResult.Failure(validation.ErrorCode!, validation.Message!, sw.Elapsed, Id);
        string? temp = null;
        try
        {
            var output = OutputPathResolver.Resolve(request);
            OutputSafety.EnsureReady(output, new FileInfo(request.InputPath).Length * 3);
            temp = Path.Combine(Path.GetDirectoryName(output)!, $".{Guid.NewGuid():N}.pdf.tmp");
            progress?.Report(new(10, "正在处理 PDF"));
            await Task.Run(() => Build(request, temp, progress, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, output, request.OverwritePolicy == OverwritePolicy.Overwrite);
            progress?.Report(new(100, "PDF 处理完成"));
            return ConversionResult.Success(output, sw.Elapsed, Id);
        }
        catch (OperationCanceledException) { return ConversionResult.Failure(ErrorCodes.Cancelled, "任务已取消。", sw.Elapsed, Id); }
        catch (PdfReaderException ex) { return ConversionResult.Failure(ErrorCodes.InvalidOrEncrypted, ex.Message, sw.Elapsed, Id); }
        catch (Exception ex) { return OutputSafety.Failure(ex, sw.Elapsed, Id); }
        finally { if (temp is not null && File.Exists(temp)) File.Delete(temp); }
    }

    private static void Build(ConversionRequest request, string outputPath, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        var options = request.Options as PdfOptions ?? new PdfOptions();
        var inputs = new[] { request.InputPath }.Concat(options.AdditionalInputs ?? []).ToArray();
        using var output = new PdfDocument();
        var completed = 0;
        foreach (var input in inputs)
        {
            token.ThrowIfCancellationRequested();
            if (Path.GetExtension(input).Equals(".pdf", StringComparison.OrdinalIgnoreCase) && options.RasterizeForCompression) AddRasterizedPdf(output, input, options, token);
            else if (Path.GetExtension(input).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) AddPdf(output, input, options, token);
            else AddImage(output, input);
            completed++;
            progress?.Report(new(10 + 80d * completed / inputs.Length, $"已处理 {completed}/{inputs.Length}"));
        }
        if (output.PageCount == 0) throw new InvalidOperationException("选定范围没有可输出的页面。");
        output.Options.CompressContentStreams = options.CompressionLevel > 0;
        output.Save(outputPath);
    }

    private static void AddRasterizedPdf(PdfDocument output, string path, PdfOptions options, CancellationToken token)
    {
        using var pdf = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
        var pages = ParsePageRange(options.PageRange, PDFtoImage.Conversion.GetPageCount(pdf, leaveOpen: true));
        foreach (var pageIndex in pages)
        {
            token.ThrowIfCancellationRequested();
            pdf.Position = 0;
            using var bitmap = PDFtoImage.Conversion.ToImage(pdf, pageIndex, leaveOpen: true, options: new RenderOptions(Dpi: options.CompressionDpi));
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, options.CompressionJpegQuality);
            using var jpeg = new MemoryStream(); encoded.SaveTo(jpeg);
            jpeg.Position = 0;
            using var image = XImage.FromStream(jpeg);
            var page = output.AddPage();
            page.Width = XUnit.FromPoint(image.PixelWidth * 72d / options.CompressionDpi);
            page.Height = XUnit.FromPoint(image.PixelHeight * 72d / options.CompressionDpi);
            using var graphics = XGraphics.FromPdfPage(page);
            graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            if (!string.IsNullOrWhiteSpace(options.Watermark)) DrawWatermark(page, options.Watermark);
        }
    }

    private static void AddPdf(PdfDocument output, string path, PdfOptions options, CancellationToken token)
    {
        using var input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var selectedPages = options.PageOrder is { Count: > 0 }
            ? options.PageOrder.Select(page => page - 1).ToArray()
            : ParsePageRange(options.PageRange, input.PageCount);
        if (selectedPages.Any(index => index < 0 || index >= input.PageCount)) throw new FormatException("页面顺序中包含无效页码。");
        foreach (var index in selectedPages)
        {
            token.ThrowIfCancellationRequested();
            var page = output.AddPage(input.Pages[index]);
            page.Rotate = NormalizeRotation(page.Rotate + options.RotationDegrees);
            if (!string.IsNullOrWhiteSpace(options.Watermark)) DrawWatermark(page, options.Watermark);
        }
    }

    private static void AddImage(PdfDocument output, string path)
    {
        using var image = XImage.FromFile(path);
        var page = output.AddPage(); page.Width = XUnit.FromPoint(image.PointWidth); page.Height = XUnit.FromPoint(image.PointHeight);
        using var graphics = XGraphics.FromPdfPage(page); graphics.DrawImage(image, 0, 0, image.PointWidth, image.PointHeight);
    }

    private static void DrawWatermark(PdfPage page, string text)
    {
        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var font = new XFont("Microsoft YaHei", 36, XFontStyleEx.Bold);
        var brush = new XSolidBrush(XColor.FromArgb(70, 90, 90, 90));
        graphics.TranslateTransform(page.Width.Point / 2, page.Height.Point / 2);
        graphics.RotateTransform(-35);
        graphics.DrawString(text, font, brush, new XRect(-page.Width.Point / 2, -30, page.Width.Point, 60), XStringFormats.Center);
    }

    internal static IReadOnlyList<int> ParsePageRange(string? range, int pageCount)
    {
        if (string.IsNullOrWhiteSpace(range)) return Enumerable.Range(0, pageCount).ToArray();
        var pages = new List<int>();
        foreach (var segment in range.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = segment.Split('-', StringSplitOptions.TrimEntries);
            if (!int.TryParse(bounds[0], out var start) || start < 1 || start > pageCount) throw new FormatException($"无效页码：{segment}");
            var end = bounds.Length == 1 ? start : int.TryParse(bounds[1], out var parsed) ? parsed : throw new FormatException($"无效页码：{segment}");
            if (end < start || end > pageCount) throw new FormatException($"无效页码范围：{segment}");
            for (var page = start; page <= end; page++) if (!pages.Contains(page - 1)) pages.Add(page - 1);
        }
        return pages.ToArray();
    }

    private static int NormalizeRotation(int degrees) => ((degrees % 360) + 360) % 360;
}
