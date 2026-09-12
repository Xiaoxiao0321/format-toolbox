using System.Diagnostics;
using FormatToolbox.Core;
using PDFtoImage;
using PDFtoImage.Exceptions;
using Tesseract;

namespace FormatToolbox.Infrastructure.Providers;

public sealed class OcrConversionProvider : IConversionProvider
{
    private static readonly HashSet<string> Inputs = new(StringComparer.OrdinalIgnoreCase) { "pdf", "png", "jpg", "jpeg", "bmp", "tif", "tiff" };
    private static readonly HashSet<string> Outputs = new(StringComparer.OrdinalIgnoreCase) { "pdf" };
    private readonly string _dataPath;
    public OcrConversionProvider(string dataPath) => _dataPath = dataPath;
    public string Id => "ocr.tesseract";
    public ConversionCapability Capability => new(Id, Inputs, Outputs, "Tesseract 5.2 + chi_sim/eng", Directory.Exists(_dataPath), Directory.Exists(_dataPath) ? null : "缺少 OCR 语言模型");
    public ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var missing = new[] { "chi_sim.traineddata", "eng.traineddata" }.Where(x => !File.Exists(Path.Combine(_dataPath, x))).ToArray();
        if (missing.Length > 0) return ValueTask.FromResult(new AvailabilityResult(false, Reason: "缺少模型：" + string.Join(", ", missing)));
        var runtime = RuntimeDependencyDetector.DetectVcppX64();
        return ValueTask.FromResult(runtime.IsAvailable ? new AvailabilityResult(true, "5.2.0") : new AvailabilityResult(false, Reason: runtime.DisplayText));
    }
    public ValidationResult Validate(ConversionRequest request) => File.Exists(request.InputPath) ? ValidationResult.Valid : new(false, ErrorCodes.FileNotFound, "找不到输入文件。");

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew(); var tempImages = new List<string>(); string? generated = null;
        try
        {
            var availability = await CheckAvailabilityAsync(cancellationToken);
            if (!availability.IsAvailable) return ConversionResult.Failure(ErrorCodes.DependencyMissing, availability.Reason!, sw.Elapsed, Id);
            var output = OutputPathResolver.Resolve(request);
            OutputSafety.EnsureReady(output, new FileInfo(request.InputPath).Length * 5);
            var images = Path.GetExtension(request.InputPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                ? await RenderPagesAsync(request, tempImages, cancellationToken) : [request.InputPath];
            var options = request.Options as OcrOptions ?? new OcrOptions();
            var outputBase = Path.Combine(Path.GetDirectoryName(output)!, $".{Guid.NewGuid():N}"); generated = outputBase + ".pdf";
            await Task.Run(() => Recognize(images, outputBase, options.Languages, progress, cancellationToken), cancellationToken);
            File.Move(generated, output, request.OverwritePolicy == OverwritePolicy.Overwrite); generated = null;
            return ConversionResult.Success(output, sw.Elapsed, Id);
        }
        catch (OperationCanceledException) { return ConversionResult.Failure(ErrorCodes.Cancelled, "OCR 已取消。", sw.Elapsed, Id); }
        catch (PdfPasswordProtectedException ex) { return ConversionResult.Failure(ErrorCodes.InvalidOrEncrypted, "PDF 已加密，无法执行 OCR：" + ex.Message, sw.Elapsed, Id); }
        catch (PdfInvalidFormatException ex) { return ConversionResult.Failure(ErrorCodes.InvalidOrEncrypted, "PDF 文件已损坏或格式无效：" + ex.Message, sw.Elapsed, Id); }
        catch (DllNotFoundException ex) { return ConversionResult.Failure(ErrorCodes.DependencyMissing, "缺少 Microsoft Visual C++ 2019 x64 Runtime：" + ex.Message, sw.Elapsed, Id); }
        catch (Exception ex) { return OutputSafety.Failure(ex, sw.Elapsed, Id); }
        finally { foreach (var file in tempImages) if (File.Exists(file)) File.Delete(file); if (generated is not null && File.Exists(generated)) File.Delete(generated); }
    }

    private static async Task<IReadOnlyList<string>> RenderPagesAsync(ConversionRequest request, List<string> temporary, CancellationToken token)
    {
        await using var pdf = new FileStream(request.InputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
        var count = Conversion.GetPageCount(pdf, leaveOpen: true);
        var pages = PageRanges.Parse((request.Options as OcrOptions)?.PageRange, count);
        foreach (var page in pages)
        {
            token.ThrowIfCancellationRequested(); var temp = Path.Combine(Path.GetTempPath(), $"FormatToolbox-{Guid.NewGuid():N}.png");
            pdf.Position = 0;
            Conversion.SavePng(temp, pdf, page, leaveOpen: true, options: new RenderOptions(Dpi: (request.Options as OcrOptions)?.Dpi ?? 300, Grayscale: true)); temporary.Add(temp);
        }
        return temporary;
    }

    private void Recognize(IReadOnlyList<string> images, string outputBase, string languages, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        using var engine = new TesseractEngine(_dataPath, languages, EngineMode.LstmOnly);
        using var renderer = ResultRenderer.CreatePdfRenderer(outputBase, _dataPath, false);
        using var document = renderer.BeginDocument("FormatToolbox OCR");
        for (var i = 0; i < images.Count; i++)
        {
            token.ThrowIfCancellationRequested(); using var pix = Pix.LoadFromFile(images[i]); using var page = engine.Process(pix);
            if (!renderer.AddPage(page)) throw new InvalidOperationException($"OCR 第 {i + 1} 页写入失败。");
            progress?.Report(new(100d * (i + 1) / images.Count, $"OCR {i + 1}/{images.Count} 页"));
        }
    }
}
