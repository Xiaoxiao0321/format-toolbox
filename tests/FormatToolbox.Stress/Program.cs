using System.Diagnostics;
using System.Text.Json;
using FormatToolbox.Core;
using FormatToolbox.Infrastructure;
using FormatToolbox.Infrastructure.Providers;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

var itemCount = ReadInt(args, "--items", 60, 1, 1000);
var pagesPerItem = ReadInt(args, "--pages", 3, 1, 100);
var rounds = ReadInt(args, "--rounds", 3, 1, 20);
var keep = args.Contains("--keep", StringComparer.OrdinalIgnoreCase);
var root = Path.Combine(Path.GetTempPath(), "FormatToolbox.Stress", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

var process = Process.GetCurrentProcess();
long peakWorkingSet = process.WorkingSet64;
using var sampler = new Timer(_ =>
{
    try { process.Refresh(); InterlockedExtensions.Max(ref peakWorkingSet, process.WorkingSet64); } catch { }
}, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));

var inputs = Enumerable.Range(0, itemCount).Select(i => CreatePdf(root, i, pagesPerItem)).ToArray();
var roundReports = new List<object>();
var totalWatch = Stopwatch.StartNew();
try
{
    for (var round = 1; round <= rounds; round++)
    {
        var output = Path.Combine(root, $"output-{round}");
        var queue = new ConversionQueue(new ConversionRegistry([new PdfConversionProvider()]), maxLocalConcurrency: 2);
        var watch = Stopwatch.StartNew();
        var items = inputs.Select(path => queue.Enqueue(new(path, "pdf", output, Options: new PdfOptions(RotationDegrees: 90)))).ToArray();
        await queue.WaitForIdleAsync().WaitAsync(TimeSpan.FromMinutes(10));
        watch.Stop();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        process.Refresh();
        var succeeded = items.Count(x => x.Status == ConversionStatus.Succeeded);
        var tempFiles = Directory.EnumerateFiles(output, ".*.tmp", SearchOption.TopDirectoryOnly).Count();
        roundReports.Add(new { round, elapsedMilliseconds = watch.ElapsedMilliseconds, succeeded, failed = items.Length - succeeded, tempFiles, workingSetBytes = process.WorkingSet64 });
        if (succeeded != items.Length || tempFiles != 0) throw new InvalidOperationException($"第 {round} 轮失败：成功 {succeeded}/{items.Length}，临时文件 {tempFiles}");
    }

    totalWatch.Stop();
    var report = new
    {
        timestamp = DateTimeOffset.Now,
        itemCount,
        pagesPerItem,
        rounds,
        totalConversions = itemCount * rounds,
        elapsedMilliseconds = totalWatch.ElapsedMilliseconds,
        peakWorkingSetBytes = peakWorkingSet,
        roundsDetail = roundReports
    };
    var reportDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "performance"));
    Directory.CreateDirectory(reportDirectory);
    var reportPath = Path.Combine(reportDirectory, "latest.json");
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"报告：{reportPath}");
}
finally
{
    sampler.Change(Timeout.Infinite, Timeout.Infinite);
    if (!keep) try { Directory.Delete(root, true); } catch { }
}

static int ReadInt(string[] values, string name, int fallback, int min, int max)
{
    var index = Array.FindIndex(values, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (index < 0) return fallback;
    if (index + 1 >= values.Length || !int.TryParse(values[index + 1], out var value) || value < min || value > max) throw new ArgumentException($"{name} 必须为 {min}–{max}。 ");
    return value;
}

static string CreatePdf(string directory, int index, int pageCount)
{
    var path = Path.Combine(directory, $"stress-{index:D4}.pdf");
    using var document = new PdfDocument();
    for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
    {
        var page = document.AddPage();
        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawString($"FormatToolbox stress {index:D4} / {pageIndex + 1}", new XFont("Arial", 18), XBrushes.Black, new XPoint(36, 72));
    }
    document.Save(path);
    return path;
}

static class InterlockedExtensions
{
    public static void Max(ref long target, long value)
    {
        long current;
        while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current) { }
    }
}
