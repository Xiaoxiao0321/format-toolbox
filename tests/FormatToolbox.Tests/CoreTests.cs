using FormatToolbox.Core;
using Xunit;
using FormatToolbox.Infrastructure.Providers;
using FormatToolbox.Infrastructure;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace FormatToolbox.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FormatToolbox.Tests", Guid.NewGuid().ToString("N"));
    public CoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Registry_resolves_formats_case_insensitively()
    {
        var provider = new FakeProvider();
        var registry = new ConversionRegistry([provider]);
        Assert.Same(provider, registry.Resolve("示例.DOCX", ".PDF"));
        Assert.Null(registry.Resolve("示例.xyz", "pdf"));
    }

    [Fact]
    public async Task Docx_to_png_reports_two_step_instructions()
    {
        var registry = new ConversionRegistry([new FakeProvider(), new PdfRenderProvider()]);
        var queue = new ConversionQueue(registry);
        var item = queue.Enqueue(new("示例.DOCX", "png"));
        await queue.WaitForIdleAsync();
        Assert.Equal(ErrorCodes.UnsupportedFormat, item.Result!.ErrorCode);
        Assert.Contains("DOCX 直接转为 PNG", item.Message);
        Assert.Contains("转 PDF", item.Message);
        Assert.Contains("PDF 导出图片", item.Message);
        Assert.Contains("Microsoft Office 或 WPS", item.Message);
        Assert.NotNull(registry.Resolve(new ConversionRequest("示例.pdf", "png")));
    }

    [Fact]
    public void Unsupported_conversion_only_suggests_existing_routes()
    {
        var registry = new ConversionRegistry([new FakeProvider()]);
        var message = registry.DescribeUnsupportedConversion(new("示例.docx", "png"));
        Assert.Contains("目标格式改为：PDF", message);
        Assert.DoesNotContain("PDF 导出图片", message);
        Assert.Contains("检查文件扩展名", registry.DescribeUnsupportedConversion(new("示例.xyz", "png")));
    }

    [Fact]
    public void Output_defaults_to_result_folder_and_never_overwrites()
    {
        var input = Path.Combine(_directory, "测试.docx"); File.WriteAllText(input, "x");
        var request = new ConversionRequest(input, "pdf");
        var first = OutputPathResolver.Resolve(request);
        Directory.CreateDirectory(Path.GetDirectoryName(first)!); File.WriteAllText(first, "existing");
        var second = OutputPathResolver.Resolve(request);
        Assert.EndsWith(Path.Combine("转换结果", "测试.pdf"), first);
        Assert.EndsWith(Path.Combine("转换结果", "测试 (1).pdf"), second);
    }

    [Fact]
    public void Existing_output_can_be_rejected()
    {
        var input = Path.Combine(_directory, "a.docx"); File.WriteAllText(input, "x");
        var output = Path.Combine(_directory, "a.pdf"); File.WriteAllText(output, "x");
        Assert.Throws<IOException>(() => OutputPathResolver.Resolve(new(input, "pdf", _directory, OverwritePolicy.Fail)));
    }

    [Fact]
    public async Task Pdfium_renders_pdf_page_to_png()
    {
        var pdf = CreateSamplePdf();
        var provider = new PdfRenderProvider();
        var result = await provider.ConvertAsync(new(pdf, "png", _directory), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Single(result.OutputFiles);
        Assert.True(new FileInfo(result.OutputFiles[0]).Length > 100);
    }

    [Fact]
    public async Task Tesseract_creates_searchable_pdf_offline()
    {
        var pdf = CreateSamplePdf();
        var dataPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FormatToolbox.Infrastructure", "tessdata"));
        var provider = new OcrConversionProvider(dataPath);
        var result = await provider.ConvertAsync(new(pdf, "pdf", _directory, Options: new OcrOptions("eng")), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.True(new FileInfo(result.OutputFiles.Single()).Length > 100);
    }

    [Fact]
    public async Task Pdf_tool_merges_inputs_in_list_order()
    {
        var first = CreateSamplePdf(); var second = CreateSamplePdf();
        var provider = new PdfConversionProvider();
        var result = await provider.ConvertAsync(new(first, "pdf", _directory, Options: new PdfOptions(AdditionalInputs: [second])), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        using var merged = PdfSharp.Pdf.IO.PdfReader.Open(result.OutputFiles.Single(), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(2, merged.PageCount);
    }

    [Fact]
    public async Task History_store_keeps_concurrent_completions()
    {
        var store = new HistoryStore(Path.Combine(_directory, "history.json"));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => store.AppendAsync(new(DateTimeOffset.Now, $"file-{i}.pdf", "png", ConversionStatus.Succeeded, null, [$"page-{i}.png"]))));
        var entries = await store.LoadAsync();
        Assert.Equal(20, entries.Count);
        Assert.Equal(20, entries.Select(x => x.InputPath).Distinct().Count());
    }

    [Fact]
    public void Output_preflight_identifies_locked_file()
    {
        var output = Path.Combine(_directory, "locked.pdf"); File.WriteAllText(output, "busy");
        using var locked = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var error = Assert.Throws<ConversionIOException>(() => OutputSafety.EnsureReady(output, 1024));
        Assert.Equal(ErrorCodes.OutputLocked, error.Code);
    }

    [Fact]
    public async Task Pdf_strong_compression_rasterizes_and_rebuilds_pages()
    {
        var input = CreateSamplePdf(); var provider = new PdfConversionProvider();
        var result = await provider.ConvertAsync(new(input, "pdf", _directory, Options: new PdfOptions(RasterizeForCompression: true, CompressionDpi: 96, CompressionJpegQuality: 50)), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        using var compressed = PdfSharp.Pdf.IO.PdfReader.Open(result.OutputFiles.Single(), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(1, compressed.PageCount);
        Assert.True(new FileInfo(result.OutputFiles.Single()).Length > 100);
    }

    [Fact]
    public async Task Pdf_page_order_can_reverse_and_remove_pages()
    {
        var input = Path.Combine(_directory, "three-pages.pdf");
        using (var document = new PdfDocument()) { document.AddPage(); document.AddPage(); document.AddPage(); document.Save(input); }
        var provider = new PdfConversionProvider();
        var result = await provider.ConvertAsync(new(input, "pdf", _directory, Options: new PdfOptions(PageOrder: [3, 1]), OutputFileName: "重新排序"), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal("重新排序.pdf", Path.GetFileName(result.OutputFiles.Single()));
        using var reordered = PdfSharp.Pdf.IO.PdfReader.Open(result.OutputFiles.Single(), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(2, reordered.PageCount);
    }

    [Fact]
    public async Task Pdf_split_supports_every_n_pages_and_named_outputs()
    {
        var input = Path.Combine(_directory, "five-pages.pdf");
        using (var document = new PdfDocument()) { for (var i = 0; i < 5; i++) document.AddPage(); document.Save(input); }
        var service = new PdfSplitService();
        var outputs = await service.SplitEveryAsync(input, _directory, "合同", 2);
        Assert.Equal(3, outputs.Count);
        Assert.All(outputs, path => Assert.StartsWith("合同-第", Path.GetFileName(path)));
    }

    [Fact]
    public async Task Pdf_split_supports_semicolon_range_groups()
    {
        var input = Path.Combine(_directory, "ranges.pdf");
        using (var document = new PdfDocument()) { for (var i = 0; i < 6; i++) document.AddPage(); document.Save(input); }
        var outputs = await new PdfSplitService().SplitRangesAsync(input, _directory, "章节", "1-2;4,6");
        Assert.Equal(2, outputs.Count);
        using var second = PdfSharp.Pdf.IO.PdfReader.Open(outputs[1], PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(2, second.PageCount);
    }

    [Fact]
    public async Task Corrupt_pdf_returns_stable_invalid_format_error()
    {
        var path = Path.Combine(_directory, "损坏.pdf"); await File.WriteAllBytesAsync(path, [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31]);
        var result = await new PdfRenderProvider().ConvertAsync(new(path, "png", _directory), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Equal(ErrorCodes.InvalidOrEncrypted, result.ErrorCode);
    }

    [Fact]
    public async Task Password_protected_pdf_is_rejected_without_bypass()
    {
        var path = Path.Combine(_directory, "加密.pdf");
        using (var document = new PdfDocument()) { document.AddPage(); document.SecuritySettings.UserPassword = "secret"; document.Save(path); }
        var result = await new PdfRenderProvider().ConvertAsync(new(path, "png", _directory), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Equal(ErrorCodes.InvalidOrEncrypted, result.ErrorCode);
    }

    [Fact]
    public async Task Unicode_long_path_pdf_converts_successfully()
    {
        var nested = Enumerable.Range(0, 8).Aggregate(_directory, (path, i) => Path.Combine(path, $"很长的中文目录-{i:D2}-abcdefghijklmnop"));
        Directory.CreateDirectory(nested); var input = Path.Combine(nested, "中文基准文件.pdf");
        using (var document = new PdfDocument()) { document.AddPage(); document.Save(input); }
        var result = await new PdfConversionProvider().ConvertAsync(new(input, "pdf"), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.True(File.Exists(result.OutputFiles.Single()));
    }

    [Fact]
    public async Task Thumbnail_baseline_has_expected_visual_dimensions()
    {
        var input = CreateSamplePdf(); var bytes = await new PdfPreviewService().RenderThumbnailAsync(input, 0);
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.InRange(decoder.Frames[0].PixelWidth, 150, 190);
        Assert.True(decoder.Frames[0].PixelHeight > decoder.Frames[0].PixelWidth);
    }

    [Fact]
    public async Task Simplified_chinese_ocr_model_creates_pdf()
    {
        var path = Path.Combine(_directory, "中文扫描样本.png");
        using (var bitmap = new SKBitmap(new SKImageInfo(1200, 400)))
        using (var canvas = new SKCanvas(bitmap))
        using (var typeface = SKTypeface.FromFamilyName("Microsoft YaHei"))
        using (var font = new SKFont(typeface, 64))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        { canvas.Clear(SKColors.White); canvas.DrawText("中文识别测试 2026", 60, 180, SKTextAlign.Left, font, paint); using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); using var file = File.Create(path); data.SaveTo(file); }
        var dataPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FormatToolbox.Infrastructure", "tessdata"));
        var result = await new OcrConversionProvider(dataPath).ConvertAsync(new(path, "pdf", _directory, Options: new OcrOptions("chi_sim", Dpi: 300), OutputFileName: "中文OCR结果"), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.True(new FileInfo(result.OutputFiles.Single()).Length > 100);
    }

    [Fact]
    public async Task Queue_releases_unsupported_tasks_without_leaking_active_state()
    {
        var queue = new ConversionQueue(new ConversionRegistry([]));
        var item = queue.Enqueue(new(Path.Combine(_directory, "unknown.xyz"), "pdf"));
        await queue.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(ConversionStatus.Failed, item.Status);
        Assert.Equal(ErrorCodes.UnsupportedFormat, item.Result?.ErrorCode);
        Assert.Equal(0, queue.ActiveCount);
    }

    [Fact]
    public async Task Queue_contains_provider_exceptions_and_continues()
    {
        var queue = new ConversionQueue(new ConversionRegistry([new ThrowingProvider()]));
        var first = queue.Enqueue(new("bad.docx", "pdf"));
        var second = queue.Enqueue(new("also-bad.docx", "pdf"));
        await queue.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.All(new[] { first, second }, item => Assert.Equal(ConversionStatus.Failed, item.Status));
        Assert.All(new[] { first, second }, item => Assert.Equal(ErrorCodes.EngineFailure, item.Result?.ErrorCode));
        Assert.Equal(0, queue.ActiveCount);
    }

    [Fact]
    public async Task Queue_limits_local_parallelism_under_pressure()
    {
        var provider = new ConcurrencyProbeProvider();
        var queue = new ConversionQueue(new ConversionRegistry([provider]), maxLocalConcurrency: 2);
        var items = Enumerable.Range(0, 40).Select(i => queue.Enqueue(new($"item-{i}.docx", "pdf"))).ToArray();
        await queue.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, provider.MaximumConcurrency);
        Assert.All(items, item => Assert.Equal(ConversionStatus.Succeeded, item.Status));
    }

    [Fact]
    public async Task Queue_cancels_a_task_waiting_for_capacity()
    {
        var provider = new ConcurrencyProbeProvider(delayMilliseconds: 250);
        var queue = new ConversionQueue(new ConversionRegistry([provider]), maxLocalConcurrency: 1);
        var first = queue.Enqueue(new("first.docx", "pdf"));
        var waiting = queue.Enqueue(new("waiting.docx", "pdf"));
        queue.Cancel(waiting.Id);
        await queue.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(ConversionStatus.Succeeded, first.Status);
        Assert.Equal(ConversionStatus.Cancelled, waiting.Status);
        Assert.Equal(0, queue.ActiveCount);
    }

    [Fact]
    public async Task Worker_dependency_detection_rejects_unregistered_com_server()
    {
        var provider = new WorkerConversionProvider("office.missing", ["docx"], "Missing Office", "FormatToolbox.Missing.Application", "missing-worker.exe");
        var result = await provider.CheckAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.False(provider.Capability.IsAvailable);
    }

    [Fact]
    public async Task Office_fallback_prefers_microsoft_engine_when_both_are_available()
    {
        var microsoft = new SelectableProvider("msoffice.word", true);
        var wps = new SelectableProvider("wps.writer", true);
        var provider = new FallbackConversionProvider("office.word", microsoft, wps, "Word 或 WPS");
        var result = await provider.ConvertAsync(new("input.docx", "pdf"), null, CancellationToken.None);
        Assert.Equal("msoffice.word", result.Engine);
        Assert.Equal(1, microsoft.ConversionCount);
        Assert.Equal(0, wps.ConversionCount);
    }

    [Fact]
    public async Task Office_fallback_uses_wps_when_microsoft_engine_is_unavailable()
    {
        var microsoft = new SelectableProvider("msoffice.word", false);
        var wps = new SelectableProvider("wps.writer", true);
        var provider = new FallbackConversionProvider("office.word", microsoft, wps, "Word 或 WPS");
        var result = await provider.ConvertAsync(new("input.docx", "pdf"), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal("wps.writer", result.Engine);
        Assert.Equal(0, microsoft.ConversionCount);
        Assert.Equal(1, wps.ConversionCount);
    }

    [Fact]
    public async Task Office_fallback_reports_one_actionable_dependency_error()
    {
        var provider = new FallbackConversionProvider("office.word", new SelectableProvider("msoffice.word", false), new SelectableProvider("wps.writer", false), "Microsoft Word 或 WPS 文字");
        var result = await provider.ConvertAsync(new("input.docx", "pdf"), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Equal(ErrorCodes.DependencyMissing, result.ErrorCode);
        Assert.Contains("Microsoft Word 或 WPS 文字", result.ErrorMessage);
    }

    private string CreateSamplePdf()
    {
        var path = Path.Combine(_directory, $"sample-{Guid.NewGuid():N}.pdf");
        using var document = new PdfDocument(); var page = document.AddPage();
        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawString("Hello OCR 123", new XFont("Arial", 24), XBrushes.Black, new XPoint(40, 80));
        document.Save(path); return path;
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class FakeProvider : IConversionProvider
    {
        public string Id => "fake";
        public ConversionCapability Capability => new(Id, new HashSet<string>(["docx"], StringComparer.OrdinalIgnoreCase), new HashSet<string>(["pdf"], StringComparer.OrdinalIgnoreCase), null, true);
        public ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AvailabilityResult(true));
        public ValidationResult Validate(ConversionRequest request) => ValidationResult.Valid;
        public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class ThrowingProvider : IConversionProvider
    {
        public string Id => "throwing";
        public ConversionCapability Capability => new(Id, new HashSet<string>(["docx"]), new HashSet<string>(["pdf"]), null, true);
        public ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AvailabilityResult(true));
        public ValidationResult Validate(ConversionRequest request) => ValidationResult.Valid;
        public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken) => throw new InvalidOperationException("sensitive diagnostic details");
    }

    private sealed class ConcurrencyProbeProvider(int delayMilliseconds = 40) : IConversionProvider
    {
        private int _current, _maximum;
        public int MaximumConcurrency => _maximum;
        public string Id => "probe.local";
        public ConversionCapability Capability => new(Id, new HashSet<string>(["docx"]), new HashSet<string>(["pdf"]), null, true);
        public ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AvailabilityResult(true));
        public ValidationResult Validate(ConversionRequest request) => ValidationResult.Valid;
        public async Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _current);
            int observed;
            while (current > (observed = _maximum)) Interlocked.CompareExchange(ref _maximum, current, observed);
            try { await Task.Delay(delayMilliseconds, cancellationToken); return ConversionResult.Success("probe.pdf", TimeSpan.Zero, Id); }
            finally { Interlocked.Decrement(ref _current); }
        }
    }

    private sealed class SelectableProvider(string id, bool available) : IConversionProvider
    {
        public int ConversionCount { get; private set; }
        public string Id => id;
        public ConversionCapability Capability => new(Id, new HashSet<string>(["docx"]), new HashSet<string>(["pdf"]), null, available);
        public ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AvailabilityResult(available));
        public ValidationResult Validate(ConversionRequest request) => ValidationResult.Valid;
        public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
        {
            ConversionCount++;
            return Task.FromResult(ConversionResult.Success("output.pdf", TimeSpan.Zero, Id));
        }
    }
}
