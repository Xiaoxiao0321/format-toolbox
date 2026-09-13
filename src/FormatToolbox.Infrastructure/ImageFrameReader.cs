using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace FormatToolbox.Infrastructure;

public static class ImageFrameReader
{
    public static IReadOnlyList<BitmapFrame> Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (!Path.GetExtension(path).Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frames = decoder.Frames.ToArray();
            foreach (var frame in frames) frame.Freeze();
            return frames;
        }
        using var codec = SKCodec.Create(stream) ?? throw new NotSupportedException("WebP 文件已损坏或格式无效。");
        if (codec.FrameCount > 1) throw new NotSupportedException("暂不支持动画 WebP；请先将动画导出为静态图片，避免丢失帧。");
        using var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw new NotSupportedException("无法完整解码 WebP 图片。");
        var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Pbgra32, null, bitmap.Bytes, bitmap.RowBytes);
        var decoded = BitmapFrame.Create(source); decoded.Freeze();
        return [decoded];
    }

    public static void SavePng(BitmapFrame frame, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(frame);
        using var output = File.Create(path); encoder.Save(output);
    }
}
