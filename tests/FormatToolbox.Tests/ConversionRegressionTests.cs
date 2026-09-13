using System.Reflection;
using System.Windows;
using FormatToolbox.App;
using FormatToolbox.Core;
using FormatToolbox.Infrastructure;
using FormatToolbox.Infrastructure.Providers;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;
using Xunit;

namespace FormatToolbox.Tests;

public sealed class ConversionRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FormatToolbox.Regression", Guid.NewGuid().ToString("N"));
    public ConversionRegressionTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Pdf_jpeg_quality_changes_encoded_output_and_dpi_changes_dimensions()
    {
        var input = CreateDetailedPdf();
        var provider = new PdfRenderProvider();
        var low = await provider.ConvertAsync(new(input, "jpg", _directory, Options: new PdfRenderOptions(Dpi: 144, JpegQuality: 10)), null, CancellationToken.None);
        var high = await provider.ConvertAsync(new(input, "jpeg", _directory, Options: new PdfRenderOptions(Dpi: 144, JpegQuality: 95)), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, low.Status);
        Assert.Equal(ConversionStatus.Succeeded, high.Status);
        Assert.True(new FileInfo(high.OutputFiles.Single()).Length > new FileInfo(low.OutputFiles.Single()).Length * 1.5);
        using var bitmap = SKBitmap.Decode(high.OutputFiles.Single());
        Assert.Equal(512, bitmap.Width);
        Assert.Equal(512, bitmap.Height);
        var png = await provider.ConvertAsync(new(input, "png", _directory, Options: new PdfRenderOptions(Dpi: 72, JpegQuality: -1)), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, png.Status);
        using var smaller = SKBitmap.Decode(png.OutputFiles.Single());
        Assert.Equal(256, smaller.Width);
    }

    [Fact]
    public void Pdf_render_runs_off_the_calling_thread()
    {
        var input = CreateDetailedPdf();
        RunSta(() =>
        {
            var callerThread = Environment.CurrentManagedThreadId;
            var progress = new ThreadProgress();
            var result = new PdfRenderProvider().ConvertAsync(new(input, "png", _directory), progress, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(ConversionStatus.Succeeded, result.Status);
            Assert.NotEqual(callerThread, progress.ThreadId);
        });
    }

    [Fact]
    public async Task Cancelled_pdf_render_does_not_publish_files()
    {
        var input = CreateDetailedPdf();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = await new PdfRenderProvider().ConvertAsync(new(input, "jpg", _directory), null, cancellation.Token);
        Assert.Equal(ConversionStatus.Cancelled, result.Status);
        Assert.Empty(Directory.GetFiles(_directory, "*.jpg"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task Queue_runs_synchronous_providers_off_caller_and_preserves_warnings()
    {
        var provider = new WarningProvider();
        var queue = new ConversionQueue(new ConversionRegistry([provider]));
        QueueItem item = null!;
        RunSta(() =>
        {
            var callerThread = Environment.CurrentManagedThreadId;
            item = queue.Enqueue(new("sample.dwg", "pdf"));
            queue.WaitForIdleAsync().GetAwaiter().GetResult();
            Assert.NotEqual(callerThread, provider.ThreadId);
        });
        Assert.Equal(ConversionStatus.Succeeded, item.Status);
        Assert.Contains("布局 A 打印失败，已跳过。", item.Message);
        Assert.Contains("有警告", new QueueRow(item).StatusText);
        var store = new HistoryStore(Path.Combine(_directory, "history.json"));
        await store.AppendAsync(new(DateTimeOffset.Now, "sample.dwg", "pdf", item.Status, null, item.Result!.OutputFiles, item.Result.Warnings));
        var history = new HistoryRow((await store.LoadAsync()).Single());
        Assert.Contains("有警告", history.StatusText);
        Assert.Contains("布局 A 打印失败，已跳过。", history.Detail);
        await File.WriteAllTextAsync(Path.Combine(_directory, "history.json"), "[{\"Timestamp\":\"2026-09-13T00:00:00Z\",\"InputPath\":\"old.pdf\",\"TargetFormat\":\"png\",\"Status\":3,\"Error\":null}]");
        Assert.Null((await store.LoadAsync()).Single().Warnings);
    }

    [Fact]
    public void Settings_follow_input_type_and_irrelevant_values_do_not_block_conversion()
    {
        RunSta(() =>
        {
            var window = new MainWindow { SelectedTarget = "png", ImageQuality = "invalid", RenderDpi = "invalid", CompressionDpi = "invalid", CompressionQuality = "invalid" };
            window.InputFiles.Add(new(Path.Combine(_directory, "image.png")));
            Assert.Equal(Visibility.Collapsed, window.JpegQualityVisibility);
            Assert.Equal(Visibility.Collapsed, window.PdfRenderSettingsVisibility);
            Assert.IsType<ImageOptions>(BuildOptions(window, "image.png", "png"));
            window.SelectedTarget = "pdf";
            Assert.IsType<PdfOptions>(BuildOptions(window, "image.png", "pdf"));
            window.InputFiles.Clear(); window.InputFiles.Add(new(Path.Combine(_directory, "drawing.dwg")));
            Assert.Equal(Visibility.Collapsed, window.PdfSettingsVisibility);
            Assert.Equal(Visibility.Visible, window.DwgWarningVisibility);
            Assert.Null(BuildOptions(window, "drawing.dwg", "pdf"));
            Assert.Null(BuildOptions(window, "document.docx", "pdf"));
            window.InputFiles.Clear(); window.InputFiles.Add(new(Path.Combine(_directory, "input.pdf")));
            window.SelectedTarget = "png"; window.RenderDpi = "144";
            Assert.Equal(Visibility.Visible, window.PdfRenderSettingsVisibility);
            Assert.IsType<PdfRenderOptions>(BuildOptions(window, "input.pdf", "png"));
            window.SelectedTarget = "jpg"; window.ImageQuality = "35";
            Assert.Equal(Visibility.Visible, window.JpegQualityVisibility);
            Assert.Equal(35, Assert.IsType<PdfRenderOptions>(BuildOptions(window, "input.pdf", "jpg")).JpegQuality);
            window.SelectedTarget = "pdf";
            Assert.Equal(Visibility.Collapsed, window.CompressionSettingsVisibility);
            Assert.IsType<PdfOptions>(BuildOptions(window, "input.pdf", "pdf"));
            window.RasterCompressPdf = true;
            Assert.Equal(Visibility.Visible, window.CompressionSettingsVisibility);
            Assert.Equal(false, TryBuildOptions(window, "input.pdf", "pdf")[0]);
            window.Close();
        });
    }

    private static object?[] TryBuildOptions(MainWindow window, string path, string target)
    {
        object?[] args = [path, target, null, null];
        var success = typeof(MainWindow).GetMethod("TryBuildOptions", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
        return [success, args[2], args[3]];
    }

    private static ConversionOptions? BuildOptions(MainWindow window, string path, string target)
    {
        var result = TryBuildOptions(window, path, target);
        Assert.True(result[0] is true, result[2]?.ToString());
        return result[1] as ConversionOptions;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private string CreateDetailedPdf()
    {
        var imagePath = Path.Combine(_directory, "detail.png");
        using (var bitmap = new SKBitmap(512, 512))
        {
            var random = new Random(42);
            bitmap.Pixels = Enumerable.Range(0, 512 * 512).Select(_ => new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256))).ToArray();
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(imagePath); encoded.SaveTo(stream);
        }
        using var document = new PdfDocument();
        var page = document.AddPage(); page.Width = XUnit.FromPoint(256); page.Height = XUnit.FromPoint(256);
        using (var image = XImage.FromFile(imagePath))
        using (var graphics = XGraphics.FromPdfPage(page)) graphics.DrawImage(image, 0, 0, 256, 256);
        var path = Path.Combine(_directory, "detail.pdf"); document.Save(path); return path;
    }

    private sealed class WarningProvider : IConversionProvider
    {
        public int ThreadId { get; private set; }
        public string Id => "autocad.test";
        public ConversionCapability Capability => new(Id, new HashSet<string> { "dwg" }, new HashSet<string> { "pdf" }, null, true);
        public ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AvailabilityResult(true));
        public ValidationResult Validate(ConversionRequest request) => ValidationResult.Valid;
        public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
        {
            ThreadId = Environment.CurrentManagedThreadId;
            progress?.Report(new(100, "转换完成"));
            return Task.FromResult(ConversionResult.Success("sample.pdf", TimeSpan.Zero, Id, "布局 A 打印失败，已跳过。"));
        }
    }

    private sealed class ThreadProgress : IProgress<ConversionProgress>
    {
        public int ThreadId { get; private set; }
        public void Report(ConversionProgress value) => ThreadId = Environment.CurrentManagedThreadId;
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
