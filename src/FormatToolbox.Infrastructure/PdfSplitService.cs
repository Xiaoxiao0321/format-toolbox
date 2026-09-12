using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using FormatToolbox.Infrastructure.Providers;

namespace FormatToolbox.Infrastructure;

public sealed class PdfSplitService
{
    public Task<IReadOnlyList<string>> SplitEveryAsync(string input, string outputDirectory, string baseName, int pagesPerFile, CancellationToken token = default)
    {
        if (pagesPerFile < 1) throw new ArgumentOutOfRangeException(nameof(pagesPerFile));
        using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        var groups = Enumerable.Range(0, source.PageCount).Chunk(pagesPerFile).Select(x => (IReadOnlyList<int>)x.ToArray()).ToArray();
        return SaveGroupsAsync(input, outputDirectory, baseName, groups, token);
    }

    public Task<IReadOnlyList<string>> SplitRangesAsync(string input, string outputDirectory, string baseName, string ranges, CancellationToken token = default)
    {
        using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        var groups = ranges.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(range => PageRanges.Parse(range, source.PageCount)).ToArray();
        if (groups.Length == 0) throw new FormatException("请填写拆分页码范围，例如 1-3;4-6。 ");
        return SaveGroupsAsync(input, outputDirectory, baseName, groups, token);
    }

    private static Task<IReadOnlyList<string>> SaveGroupsAsync(string input, string directory, string baseName, IReadOnlyList<IReadOnlyList<int>> groups, CancellationToken token)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            Directory.CreateDirectory(directory); var outputs = new List<string>();
            using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            for (var i = 0; i < groups.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var request = new FormatToolbox.Core.ConversionRequest(input, "pdf", directory, OutputFileName: $"{baseName}-第{i + 1}部分");
                var output = FormatToolbox.Core.OutputPathResolver.Resolve(request);
                OutputSafety.EnsureReady(output, new FileInfo(input).Length);
                var temp = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
                try
                {
                    using var document = new PdfDocument(); foreach (var page in groups[i]) document.AddPage(source.Pages[page]); document.Save(temp); File.Move(temp, output); outputs.Add(output);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            return outputs;
        }, token);
    }
}
