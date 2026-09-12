using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: IconBuilder <input.png> <output.ico>");
    return 2;
}

var source = new BitmapImage();
source.BeginInit();
source.CacheOption = BitmapCacheOption.OnLoad;
source.UriSource = new Uri(Path.GetFullPath(args[0]));
source.EndInit();
source.Freeze();

var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
var frames = new List<byte[]>();
foreach (var size in sizes)
{
    var scale = Math.Min((double)size / source.PixelWidth, (double)size / source.PixelHeight);
    var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(resized));
    using var stream = new MemoryStream();
    encoder.Save(stream);
    frames.Add(stream.ToArray());
}

using var output = File.Create(args[1]);
using var writer = new BinaryWriter(output);
writer.Write((ushort)0);
writer.Write((ushort)1);
writer.Write((ushort)sizes.Length);
var offset = 6 + sizes.Length * 16;
for (var i = 0; i < sizes.Length; i++)
{
    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write(frames[i].Length);
    writer.Write(offset);
    offset += frames[i].Length;
}
foreach (var frame in frames) writer.Write(frame);
return 0;
