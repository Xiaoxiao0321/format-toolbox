using System.Windows.Media;
using System.Windows.Media.Imaging;
using FormatToolbox.Core;
using FormatToolbox.Infrastructure;
using FormatToolbox.Infrastructure.Providers;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Xunit;

namespace FormatToolbox.Tests;

public sealed class ImageFrameTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"FormatToolbox.Frames",Guid.NewGuid().ToString("N"));
    public ImageFrameTests()=>Directory.CreateDirectory(_directory);
    [Theory]
    [InlineData("png",3)]
    [InlineData("jpg",3)]
    [InlineData("tiff",1)]
    public async Task Multi_page_TIFF_preserves_all_frames_and_order(string target,int outputs)
    {
        var input=CreateTiff();var result=await new ImageConversionProvider().ConvertAsync(new(input,target,_directory),null,CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded,result.Status);Assert.Equal(outputs,result.OutputFiles.Count);
        var frames=result.OutputFiles.SelectMany(ImageFrameReader.Read).ToArray();Assert.Equal(3,frames.Length);
        Assert.Equal(new[]{32,48,64},frames.Select(f=>f.PixelWidth));
    }
    [Fact]
    public async Task Multi_page_TIFF_to_PDF_preserves_all_pages()
    {
        var result=await new PdfConversionProvider().ConvertAsync(new(CreateTiff(),"pdf",_directory),null,CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded,result.Status);using var pdf=PdfReader.Open(result.OutputFiles.Single(),PdfDocumentOpenMode.Import);Assert.Equal(3,pdf.PageCount);
    }
    [Fact]
    public async Task Cancelling_after_first_frame_reports_saved_result_as_partial()
    {
        using var token=new CancellationTokenSource();
        var result=await new ImageConversionProvider().ConvertAsync(new(CreateTiff(),"png",_directory),new Callback(p=>{if(p.Percent>10)token.Cancel();}),token.Token);
        Assert.Equal(ConversionStatus.PartialSucceeded,result.Status);Assert.Single(result.OutputFiles);Assert.Contains("1/3",result.ErrorMessage);Assert.Empty(Directory.GetFiles(_directory,"*.tmp"));
    }
    [Fact]
    public async Task WebP_uses_bundled_decoder_and_preserves_transparency()
    {
        var input=Path.Combine(_directory,"alpha.webp");using(var b=new SKBitmap(64,48)){b.Erase(SKColors.Transparent);b.SetPixel(20,20,new SKColor(200,30,60,128));using var data=b.Encode(SKEncodedImageFormat.Webp,100);using var file=File.Create(input);data.SaveTo(file);}
        var result=await new ImageConversionProvider().ConvertAsync(new(input,"png",_directory),null,CancellationToken.None);
        Assert.Equal(ConversionStatus.Succeeded,result.Status);using var image=SKBitmap.Decode(result.OutputFiles.Single());Assert.Equal(0,image.GetPixel(0,0).Alpha);Assert.InRange(image.GetPixel(20,20).Alpha,127,129);
    }
    private string CreateTiff()
    {
        var path=Path.Combine(_directory,"pages.tiff");var encoder=new TiffBitmapEncoder();
        foreach(var width in new[]{32,48,64}){var bytes=Enumerable.Repeat((byte)255,width*32*4).ToArray();var source=BitmapSource.Create(width,32,200,200,PixelFormats.Bgra32,null,bytes,width*4);source.Freeze();encoder.Frames.Add(BitmapFrame.Create(source));}
        using var stream=File.Create(path);encoder.Save(stream);return path;
    }
    private sealed class Callback(Action<ConversionProgress> callback):IProgress<ConversionProgress>{public void Report(ConversionProgress value)=>callback(value);}
    public void Dispose()=>Directory.Delete(_directory,true);
}
