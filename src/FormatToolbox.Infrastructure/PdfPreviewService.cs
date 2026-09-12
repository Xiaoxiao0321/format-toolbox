using PDFtoImage;

namespace FormatToolbox.Infrastructure;

public sealed class PdfPreviewService
{
    public int GetPageCount(string path)
    {
        using var pdf = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
        return Conversion.GetPageCount(pdf, leaveOpen: false);
    }

    public async Task<byte[]> RenderThumbnailAsync(string path, int zeroBasedPage, CancellationToken cancellationToken = default)
    {
        await using var pdf = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        using var stream = new MemoryStream();
        await Task.Run(() => Conversion.SaveJpeg(stream, pdf, zeroBasedPage, leaveOpen: true, options: new RenderOptions(Width: 180, WithAspectRatio: true)), cancellationToken);
        return stream.ToArray();
    }
}
