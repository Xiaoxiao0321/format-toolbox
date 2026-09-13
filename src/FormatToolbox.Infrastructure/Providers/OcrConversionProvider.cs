using System.Diagnostics;
using FormatToolbox.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFtoImage;
using PDFtoImage.Exceptions;
using Tesseract;

namespace FormatToolbox.Infrastructure.Providers;

public sealed class OcrConversionProvider : IConversionProvider
{
    private static readonly HashSet<string> Inputs = new(StringComparer.OrdinalIgnoreCase) { "pdf", "png", "jpg", "jpeg", "bmp", "tif", "tiff", "webp" };
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
        var sw = Stopwatch.StartNew(); var tempImages = new List<string>(); string? generated = null; string? textLayer = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validation = Validate(request);
            if (!validation.IsValid) return ConversionResult.Failure(validation.ErrorCode!, validation.Message!, sw.Elapsed, Id);
            var availability = await CheckAvailabilityAsync(cancellationToken);
            if (!availability.IsAvailable) return ConversionResult.Failure(ErrorCodes.DependencyMissing, availability.Reason!, sw.Elapsed, Id);
            var output = OutputPathResolver.Resolve(request);
            OutputSafety.EnsureReady(output, new FileInfo(request.InputPath).Length * 5);
            var isPdf = Path.GetExtension(request.InputPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
            var images = isPdf
                ? await RenderPagesAsync(request, tempImages, cancellationToken)
                : await Task.Run(() => PrepareImages(request, tempImages, cancellationToken));
            var options = request.Options as OcrOptions ?? new OcrOptions();
            var outputBase = Path.Combine(Path.GetDirectoryName(output)!, $".{Guid.NewGuid():N}"); generated = outputBase + ".pdf";
            var preserveOriginal = isPdf && !options.Grayscale;
            await Task.Run(() => Recognize(images, outputBase, options, preserveOriginal, isPdf, progress, cancellationToken), cancellationToken);
            if (preserveOriginal)
            {
                textLayer = generated;
                generated = outputBase + ".combined.pdf";
                await Task.Run(() => AddTextLayer(request.InputPath, textLayer, generated, options, cancellationToken), cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(generated, output, request.OverwritePolicy == OverwritePolicy.Overwrite); generated = null;
            return ConversionResult.Success(output, sw.Elapsed, Id);
        }
        catch (OperationCanceledException) { return ConversionResult.Failure(ErrorCodes.Cancelled, "OCR 已取消。", sw.Elapsed, Id); }
        catch (PdfPasswordProtectedException ex) { return ConversionResult.Failure(ErrorCodes.InvalidOrEncrypted, "PDF 已加密，无法执行 OCR：" + ex.Message, sw.Elapsed, Id); }
        catch (PdfInvalidFormatException ex) { return ConversionResult.Failure(ErrorCodes.InvalidOrEncrypted, "PDF 文件已损坏或格式无效：" + ex.Message, sw.Elapsed, Id); }
        catch (DllNotFoundException ex) { return ConversionResult.Failure(ErrorCodes.DependencyMissing, "缺少 Microsoft Visual C++ 2019 x64 Runtime：" + ex.Message, sw.Elapsed, Id); }
        catch (NotSupportedException ex) { return ConversionResult.Failure(ErrorCodes.UnsupportedFormat, ex.Message, sw.Elapsed, Id); }
        catch (Exception ex) { return OutputSafety.Failure(ex, sw.Elapsed, Id); }
        finally
        {
            foreach (var file in tempImages) await TemporaryFileCleanup.DeleteFileAsync(file, Id);
            if (generated is not null) await TemporaryFileCleanup.DeleteFileAsync(generated, Id);
            if (textLayer is not null) await TemporaryFileCleanup.DeleteFileAsync(textLayer, Id);
        }
    }

    private static Task<IReadOnlyList<string>> RenderPagesAsync(ConversionRequest request, List<string> temporary, CancellationToken token)
        => Task.Run<IReadOnlyList<string>>(() => RenderPages(request, temporary, token));

    private static IReadOnlyList<string> PrepareImages(ConversionRequest request, List<string> temporary, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var frames = ImageFrameReader.Read(request.InputPath);
        if (frames.Count == 1 && !Path.GetExtension(request.InputPath).Equals(".webp", StringComparison.OrdinalIgnoreCase)) return [request.InputPath];
        foreach (var frame in frames)
        {
            token.ThrowIfCancellationRequested();
            var path = Path.Combine(Path.GetTempPath(), $"FormatToolbox-{Guid.NewGuid():N}.png"); temporary.Add(path);
            ImageFrameReader.SavePng(frame, path);
        }
        return temporary;
    }

    private static IReadOnlyList<string> RenderPages(ConversionRequest request, List<string> temporary, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var pdf = new FileStream(request.InputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
        var count = Conversion.GetPageCount(pdf, leaveOpen: true);
        var pages = PageRanges.Parse((request.Options as OcrOptions)?.PageRange, count);
        foreach (var page in pages)
        {
            token.ThrowIfCancellationRequested(); var temp = Path.Combine(Path.GetTempPath(), $"FormatToolbox-{Guid.NewGuid():N}.png");
            pdf.Position = 0;
            temporary.Add(temp);
            Conversion.SavePng(temp, pdf, page, leaveOpen: true, options: new RenderOptions(Dpi: (request.Options as OcrOptions)?.Dpi ?? 300, Grayscale: (request.Options as OcrOptions)?.Grayscale ?? false));
        }
        return temporary;
    }

    private void Recognize(IReadOnlyList<string> images, string outputBase, OcrOptions options, bool textOnly, bool renderedPdf, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        using var outputScope = NativeOutputSynchronization.EnterOcrOutput(token);
        using var engine = new TesseractEngine(_dataPath, options.Languages, EngineMode.LstmOnly);
        using var renderer = ResultRenderer.CreatePdfRenderer(outputBase, _dataPath, textOnly);
        using var document = renderer.BeginDocument("FormatToolbox OCR");
        for (var i = 0; i < images.Count; i++)
        {
            token.ThrowIfCancellationRequested(); using var pix = Pix.LoadFromFile(images[i]);
            using var gray = options.Grayscale ? pix.ConvertTo8(0) : null;
            var recognitionImage = gray ?? pix;
            if (renderedPdf || recognitionImage.XRes <= 0) recognitionImage.XRes = options.Dpi;
            if (renderedPdf || recognitionImage.YRes <= 0) recognitionImage.YRes = options.Dpi;
            using var page = engine.Process(recognitionImage);
            if (!renderer.AddPage(page)) throw new InvalidOperationException($"OCR 第 {i + 1} 页写入失败。");
            progress?.Report(new(100d * (i + 1) / images.Count, $"OCR {i + 1}/{images.Count} 页"));
        }
    }

    private static void AddTextLayer(string inputPath, string textPath, string outputPath, OcrOptions options, CancellationToken token)
    {
        using var input = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();
        using var text = XPdfForm.FromFile(textPath);
        var selected = PageRanges.Parse(options.PageRange, input.PageCount).ToArray();
        if (selected.Length != text.PageCount) throw new InvalidOperationException("OCR 文字层页数与原页面不一致。");
        for (var i = 0; i < selected.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var page = output.AddPage(input.Pages[selected[i]]);
            var rotation = ((page.Rotate % 360) + 360) % 360;
            var crop = page.Elements.ContainsKey("/CropBox") ? page.CropBox : page.MediaBox;
            var width = crop.Width; var height = crop.Height;
            page.Rotate = 0;
            using (var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
            {
                graphics.TranslateTransform(crop.X1, page.MediaBox.Height - crop.Y2);
                switch (rotation)
                {
                    case 90: graphics.TranslateTransform(0, height); graphics.RotateTransform(-90); break;
                    case 180: graphics.TranslateTransform(width, height); graphics.RotateTransform(180); break;
                    case 270: graphics.TranslateTransform(width, 0); graphics.RotateTransform(90); break;
                }
                text.PageNumber = i + 1;
                graphics.DrawImage(text, 0, 0, rotation is 90 or 270 ? height : width, rotation is 90 or 270 ? width : height);
            }
            page.Rotate = rotation;
        }
        output.Save(outputPath);
    }
}
