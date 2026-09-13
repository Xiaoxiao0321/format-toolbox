using System.Diagnostics;
using FormatToolbox.Core;
using FormatToolbox.Infrastructure.Providers;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FormatToolbox.Infrastructure;

public sealed class PdfSplitService
{
    public Task<IReadOnlyList<string>> SplitEveryAsync(string input, string outputDirectory, string baseName, int pagesPerFile, CancellationToken token = default)
        => GetOutputsAsync(new(input, "pdf", outputDirectory, Options: new PdfSplitOptions(pagesPerFile), OutputFileName: baseName), token);

    public Task<IReadOnlyList<string>> SplitRangesAsync(string input, string outputDirectory, string baseName, string ranges, CancellationToken token = default)
    {
        return GetOutputsAsync(new(input, "pdf", outputDirectory, Options: new PdfSplitOptions(PageRanges: ranges), OutputFileName: baseName), token);
    }

    private async Task<IReadOnlyList<string>> GetOutputsAsync(ConversionRequest request, CancellationToken token)
    {
        var result = await SplitAsync(request, null, token);
        if (result.Status == ConversionStatus.Succeeded) return result.OutputFiles;
        if (result.ErrorCode == ErrorCodes.Cancelled) throw new OperationCanceledException(result.ErrorMessage, token);
        throw new IOException(result.ErrorMessage);
    }

    public Task<ConversionResult> SplitAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken token)
        => Task.Run(() => Split(request, progress, token));

    private static ConversionResult Split(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var outputs = new List<string>();
        int? total = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var options = request.Options as PdfSplitOptions ?? new PdfSplitOptions();
            if (options.PagesPerFile < 1) throw new FormatException("每 N 页必须填写大于 0 的整数。");
            using var source = PdfReader.Open(request.InputPath, PdfDocumentOpenMode.Import);
            IReadOnlyList<IReadOnlyList<int>> groups = options.PageRanges is null
                ? Enumerable.Range(0, source.PageCount).Chunk(options.PagesPerFile).Select(x => (IReadOnlyList<int>)x).ToArray()
                : options.PageRanges.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(range => PageRanges.Parse(range, source.PageCount)).ToArray();
            if (groups.Count == 0 || groups.Any(x => x.Count == 0)) throw new FormatException("请填写有效的拆分页码范围，例如 1-3;4-6。");
            total = groups.Count;
            var baseName = string.IsNullOrWhiteSpace(request.OutputFileName) ? Path.GetFileNameWithoutExtension(request.InputPath) : request.OutputFileName;
            for (var i = 0; i < groups.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var output = OutputPathResolver.Resolve(request with { OutputFileName = $"{baseName}-第{i + 1}部分" });
                OutputSafety.EnsureReady(output, new FileInfo(request.InputPath).Length);
                var temp = Path.Combine(Path.GetDirectoryName(output)!, $".{Guid.NewGuid():N}.tmp");
                try
                {
                    using var document = new PdfDocument();
                    foreach (var page in groups[i]) { token.ThrowIfCancellationRequested(); document.AddPage(source.Pages[page]); }
                    document.Save(temp);
                    token.ThrowIfCancellationRequested();
                    File.Move(temp, output, request.OverwritePolicy == OverwritePolicy.Overwrite);
                    outputs.Add(output);
                    progress?.Report(new(100d * outputs.Count / groups.Count, $"已拆分 {outputs.Count}/{groups.Count} 个文件"));
                }
                finally { TemporaryFileCleanup.DeleteFile(temp, "pdf.split"); }
            }
            return new(ConversionStatus.Succeeded, outputs, [], null, null, watch.Elapsed, "pdf.split");
        }
        catch (OperationCanceledException) { return ConversionResult.Failure(ErrorCodes.Cancelled, "拆分已取消。", watch.Elapsed, "pdf.split").WithCompletedOutputs(outputs, total); }
        catch (Exception ex) { return OutputSafety.Failure(ex, watch.Elapsed, "pdf.split").WithCompletedOutputs(outputs, total); }
    }
}
