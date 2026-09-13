using FormatToolbox.Core;
using FormatToolbox.Infrastructure.Providers;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFtoImage;
using SkiaSharp;
using Xunit;

namespace FormatToolbox.Tests;

public sealed class OcrColorTests
{
    private static readonly string DataPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/FormatToolbox.Infrastructure/tessdata"));
    private static readonly string Artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/acceptance/ocr-color"));

    [Fact]
    public async Task Default_preserves_original_page_images_crop_and_rotation()
    {
        Directory.CreateDirectory(Artifacts);
        var input = CreatePdf("original.pdf");
        var result = await new OcrConversionProvider(DataPath).ConvertAsync(new(input, "pdf", Artifacts, OverwritePolicy.Overwrite, new OcrOptions("eng", Dpi: 144), "color"), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        using var before = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        using var after = PdfReader.Open(result.OutputFiles.Single(), PdfDocumentOpenMode.Import);
        Assert.Equal(4, after.PageCount);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(before.Pages[i].MediaBox, after.Pages[i].MediaBox);
            Assert.Equal(before.Pages[i].CropBox, after.Pages[i].CropBox);
            Assert.Equal(before.Pages[i].Rotate, after.Pages[i].Rotate);
            using var source = Conversion.ToImage(File.ReadAllBytes(input), i, options: new RenderOptions(Dpi: 144));
            using var actual = Conversion.ToImage(File.ReadAllBytes(result.OutputFiles.Single()), i, options: new RenderOptions(Dpi: 144));
            Assert.Equal(source.Width, actual.Width); Assert.Equal(source.Height, actual.Height);
            Assert.Equal(source.Pixels, actual.Pixels);
        }
    }

    [Fact]
    public async Task Grayscale_is_explicit_and_removes_page_color()
    {
        Directory.CreateDirectory(Artifacts);
        var input = CreatePdf("gray-original.pdf");
        var result = await new OcrConversionProvider(DataPath).ConvertAsync(new(input, "pdf", Artifacts, OverwritePolicy.Overwrite, new OcrOptions("eng", "1", 144, Grayscale: true), "gray"), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        using var actual = Conversion.ToImage(File.ReadAllBytes(result.OutputFiles.Single()), 0, options: new RenderOptions(Dpi: 144));
        Assert.All(actual.Pixels, pixel => { Assert.InRange(Math.Abs(pixel.Red - pixel.Green), 0, 1); Assert.InRange(Math.Abs(pixel.Green - pixel.Blue), 0, 1); });
        using var document = PdfReader.Open(result.OutputFiles.Single(), PdfDocumentOpenMode.Import);
        Assert.Single(document.Pages.Cast<PdfPage>());
    }

    [Fact]
    public async Task Selected_page_range_keeps_matching_original_pages()
    {
        Directory.CreateDirectory(Artifacts);
        var input = CreatePdf("range-original.pdf");
        var result = await new OcrConversionProvider(DataPath).ConvertAsync(new(input, "pdf", Artifacts, OverwritePolicy.Overwrite, new OcrOptions("eng", "2,4", 144), "range"), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        using var document = PdfReader.Open(result.OutputFiles.Single(), PdfDocumentOpenMode.Import);
        Assert.Equal(2, document.PageCount);
        Assert.Equal(90, document.Pages[0].Rotate); Assert.Equal(270, document.Pages[1].Rotate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Image_input_honors_explicit_grayscale_choice(bool grayscale)
    {
        Directory.CreateDirectory(Artifacts);
        var input = CreatePdf(grayscale ? "image-gray-original.pdf" : "image-color-original.pdf") + ".png";
        var result = await new OcrConversionProvider(DataPath).ConvertAsync(new(input, "pdf", Artifacts, OverwritePolicy.Overwrite, new OcrOptions("eng", Dpi: 144, Grayscale: grayscale), grayscale ? "image-gray" : "image-color"), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        using var actual = Conversion.ToImage(File.ReadAllBytes(result.OutputFiles.Single()), 0, options: new RenderOptions(Dpi: 144));
        var hasColor = actual.Pixels.Any(pixel => Math.Abs(pixel.Red - pixel.Green) > 20 || Math.Abs(pixel.Green - pixel.Blue) > 20);
        Assert.Equal(!grayscale, hasColor);
    }

    private static string CreatePdf(string name)
    {
        var imagePath = Path.Combine(Artifacts, name + ".png");
        using (var bitmap = new SKBitmap(1200, 720))
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { IsAntialias = true })
        using (var font = new SKFont(SKTypeface.FromFamilyName("Arial"), 64))
        {
            canvas.Clear(SKColors.White);
            foreach (var (color, x) in new[] { (SKColors.Red, 100), (SKColors.Green, 430), (SKColors.Blue, 760) })
            { paint.Color = color; canvas.DrawRect(x, 120, 280, 150, paint); }
            paint.Color = SKColors.Black;
            canvas.DrawText("COLOR OCR TEST 2026", 110, 470, SKTextAlign.Left, font, paint);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(imagePath); data.SaveTo(stream);
        }
        using var image = XImage.FromFile(imagePath);
        using var document = new PdfDocument();
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var page = document.AddPage(); page.Width = XUnit.FromPoint(400); page.Height = XUnit.FromPoint(240);
            page.CropBox = new PdfRectangle(new XPoint(20, 10), new XPoint(380, 230));
            using (var graphics = XGraphics.FromPdfPage(page)) graphics.DrawImage(image, 0, 0, 400, 240);
            page.Rotate = rotation;
        }
        var path = Path.Combine(Artifacts, name); document.Save(path); return path;
    }
}
