using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FormatToolbox.App;
using Xunit;

namespace FormatToolbox.Tests;

public sealed class ThemeTests
{
    [Fact]
    public void Reading_colors_meet_contrast_in_every_theme_state()
    {
        RunSta(() =>
        {
            var resources = new ResourceDictionary { Source = new Uri("/格式转换工具箱;component/Themes/Light.xaml", UriKind.Relative) };
            (string Text, string Background)[] pairs =
            [
                ("TextBrush", "WindowBrush"), ("TextBrush", "SurfaceBrush"), ("TextBrush", "SelectionBrush"),
                ("MutedBrush", "WindowBrush"), ("MutedBrush", "SurfaceBrush"), ("MutedBrush", "DisabledBrush"),
                ("PrimaryTextBrush", "PrimaryBrush"), ("PrimaryTextBrush", "PrimaryHoverBrush"), ("PrimaryTextBrush", "PrimaryPressedBrush"),
                ("SecondaryTextBrush", "SecondaryBrush"), ("SecondaryTextBrush", "SecondaryHoverBrush"), ("SecondaryTextBrush", "SecondaryPressedBrush"),
                ("WarningBrush", "WarningSurfaceBrush"), ("WarningBrush", "WindowBrush"),
                ("ErrorBrush", "ErrorSurfaceBrush")
            ];
            foreach (var (text, background) in pairs)
            {
                var a = Luminance(((SolidColorBrush)resources[text]).Color);
                var b = Luminance(((SolidColorBrush)resources[background]).Color);
                var contrast = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
                Assert.True(contrast >= 4.5, $"{text} / {background}: {contrast:F2}:1");
            }
        });
    }

    [Fact]
    public void Standalone_windows_load_templates_and_render_at_three_pixel_densities()
    {
        RunSta(() =>
        {
            var output = Environment.GetEnvironmentVariable("FORMATTOOLBOX_THEME_PREVIEWS");
            if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);
            Func<Window>[] factories =
            [
                () => new MainWindow(), () => new PdfToolsWindow(_ => { }, null),
                () => new ConfirmationWindow(string.Concat(Enumerable.Repeat("长提示文本：请检查转换结果和任务消息。\n", 15)), "确认操作"),
                () => new FeedbackWindow([]), () => new AboutWindow()
            ];
            foreach (var factory in factories)
            {
                var window = factory();
                try
                {
                    Assert.NotNull(window.FindResource("ThemeWindow"));
                    Assert.NotNull(window.Icon);
                    var root = (FrameworkElement)window.Content;
                    var size = new Size(window.Width - 40, window.Height - 80);
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                    var buttons = Descendants(root).OfType<Button>().Where(b => b.IsVisible || b.Visibility == Visibility.Visible).ToArray();
                    Assert.NotEmpty(buttons);
                    foreach (var button in buttons)
                    {
                        Assert.NotNull(button.Template);
                        Assert.True(button.MinHeight >= 34);
                        // Layout rounding can move an edge by a fraction of a DIP at 125% DPI.
                        if (button.ActualHeight > 0) Assert.True(button.ActualHeight >= 33, $"{button.Content}: {button.ActualHeight:F2} DIP");
                    }
                    foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                    {
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                        var background = new DrawingVisual();
                        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(size));
                        bitmap.Render(background);
                        bitmap.Render(root);
                        Assert.Equal((int)Math.Ceiling(size.Width * scale), bitmap.PixelWidth);
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var stream = File.Create(Path.Combine(output, $"{window.GetType().Name}-{scale * 100:0}.png"));
                            encoder.Save(stream);
                        }
                    }
                    var minimum = new Size(window.MinWidth - 30, window.MinHeight - 40);
                    root.Measure(minimum); root.Arrange(new Rect(minimum)); root.UpdateLayout();
                    foreach (var button in Descendants(root).OfType<Button>().Where(b => b.Visibility == Visibility.Visible && b.ActualHeight > 0))
                        Assert.True(button.ActualHeight >= 33, $"{window.GetType().Name} minimum size: {button.Content}: {button.ActualHeight:F2} DIP");
                }
                finally { window.Close(); }
            }
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value) { var c = value / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
