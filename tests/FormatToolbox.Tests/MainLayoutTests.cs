using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FormatToolbox.App;
using Xunit;

namespace FormatToolbox.Tests;

public sealed class MainLayoutTests
{
    [Fact]
    public void Main_window_reflows_without_losing_settings_or_file_space()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                try
                {
                    var root = (FrameworkElement)window.Content;
                    var file = (GroupBox)window.FindName("FileGroup")!;
                    var settings = (GroupBox)window.FindName("SettingsPanel")!;
                    var vertical = (Thumb)window.FindName("WorkSplitter")!;
                    var horizontal = (Thumb)window.FindName("ResultsSplitter")!;
                    var advanced = (Expander)window.FindName("AdvancedSettingsExpander")!;
                    var more = (Button)window.FindName("MoreActionsButton")!;
                    var layout = (Grid)window.FindName("MainLayout")!;
                    var scroll = (ScrollViewer)window.FindName("MainContentScroll")!;
                    var input = (ListView)window.FindName("InputList")!;
                    var history = (ListView)window.FindName("HistoryList")!;
                    var results = (TabControl)window.FindName("ResultsTabs")!;
                    var pathColumn = ((GridView)input.View).Columns[2];
                    Assert.Equal(4, more.ContextMenu!.Items.Count);
                    Assert.Equal(new[] { "ocr", "merge", "pdf-tools", "dwg" },
                        more.ContextMenu.Items.Cast<MenuItem>().Select(x => x.Tag));
                    Assert.Same(window.FindResource("ThemeContextMenu"), more.ContextMenu.Style);
                    foreach (MenuItem item in more.ContextMenu.Items)
                        Assert.Same(window.FindResource("ThemeMenuItem"), item.Style);
                    more.ContextMenu.ApplyTemplate();
                    Assert.Equal(new CornerRadius(8), ((Border)VisualTreeHelper.GetChild(more.ContextMenu, 0)).CornerRadius);
                    Assert.Equal(ScrollBarVisibility.Auto, input.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
                    Assert.Equal(ScrollBarVisibility.Auto, history.GetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty));

                    Arrange(root, 1140, 780);
                    Assert.Equal(Visibility.Visible, vertical.Visibility);
                    Assert.InRange(settings.ActualWidth, 270, 420);
                    Assert.True(file.ActualWidth >= 520);
                    var widePathWidth = pathColumn.Width;
                    Assert.True(file.TranslatePoint(new Point(), layout).X < settings.TranslatePoint(new Point(), layout).X);
                    var settingsWidth = settings.ActualWidth;
                    Drag(vertical, -4, 0);
                    root.UpdateLayout();
                    Assert.True(settings.ActualWidth > settingsWidth + 2, $"Settings width: {settingsWidth} -> {settings.ActualWidth}");
                    var afterSmallDrag = settings.ActualWidth;
                    DragInSteps(vertical, root, -24, 0, 3);
                    Assert.True(settings.ActualWidth > afterSmallDrag + 45, $"Settings width after repeated drag: {afterSmallDrag} -> {settings.ActualWidth}");
                    Drag(vertical, -500, 0);
                    root.UpdateLayout();
                    Assert.InRange(settings.ActualWidth, 418, 420);
                    Drag(vertical, 40, 0);
                    root.UpdateLayout();
                    var resultsHeight = results.ActualHeight;
                    Drag(horizontal, 0, -4);
                    root.UpdateLayout();
                    Assert.True(results.ActualHeight > resultsHeight + 2, $"Results height: {resultsHeight} -> {results.ActualHeight}");
                    var afterSmallVerticalDrag = results.ActualHeight;
                    DragInSteps(horizontal, root, 0, -24, 3);
                    Assert.True(results.ActualHeight > afterSmallVerticalDrag + 45, $"Results height after repeated drag: {afterSmallVerticalDrag} -> {results.ActualHeight}");
                    Drag(horizontal, 0, -500);
                    root.UpdateLayout();
                    Assert.InRange(((Grid)window.FindName("MainLayout")!).RowDefinitions[1].ActualHeight, 330, 335);
                    Drag(horizontal, 0, 20);
                    root.UpdateLayout();
                    var adjustedSettingsWidth = settings.ActualWidth;
                    var adjustedResultsHeight = results.ActualHeight;
                    results.SelectedIndex = 1;
                    Arrange(root, 1140, 780);
                    var historyToolbar = (WrapPanel)((Grid)((TabItem)results.Items[1]).Content).Children[0];
                    Assert.Equal(4, historyToolbar.Children.Count);
                    foreach (Button button in historyToolbar.Children)
                    {
                        Assert.True(button.ActualHeight >= 33);
                        Assert.InRange(button.TranslatePoint(new Point(button.ActualWidth, 0), results).X, 0, results.ActualWidth);
                    }
                    Assert.True(history.ActualHeight >= 75);

                    window.InputFiles.Add(new FileInfo(Path.Combine(Path.GetTempPath(), new string('x', 80) + ".pdf")));
                    window.RasterCompressPdf = true;
                    advanced.IsExpanded = true;
                    Arrange(root, 1140, 780);
                    Assert.True(((ScrollViewer)settings.Content).ScrollableHeight > 0);
                    window.ImageQuality = "81";
                    window.PdfPageRange = "1,3-5";
                    window.SelectedTarget = "jpg";
                    Assert.False(advanced.IsExpanded);
                    Assert.Equal("81", window.ImageQuality);
                    foreach (Button button in historyToolbar.Children)
                        Assert.InRange(button.TranslatePoint(new Point(button.ActualWidth, 0), results).X, 0, results.ActualWidth);
                    Assert.Equal("1,3-5", window.PdfPageRange);

                    Arrange(root, 620, 490);
                    Assert.Equal(Visibility.Collapsed, vertical.Visibility);
                    Assert.InRange(layout.ActualWidth, 560, 650);
                    Assert.True(settings.TranslatePoint(new Point(), layout).Y < file.TranslatePoint(new Point(), layout).Y);
                    Assert.True(file.ActualWidth >= 550);
                    Assert.True(scroll.ScrollableHeight > 0);
                    Assert.True(pathColumn.Width < widePathWidth);
                    Assert.Equal("81", window.ImageQuality);

                    var output = Environment.GetEnvironmentVariable("FORMATTOOLBOX_THEME_PREVIEWS");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Directory.CreateDirectory(output);
                        more.ContextMenu.Measure(new Size(250, 210));
                        var menuHeight = (int)Math.Ceiling(more.ContextMenu.DesiredSize.Height);
                        more.ContextMenu.Arrange(new Rect(0, 0, 250, menuHeight));
                        more.ContextMenu.UpdateLayout();
                        var menuBitmap = new RenderTargetBitmap(250, menuHeight, 96, 96, PixelFormats.Pbgra32);
                        menuBitmap.Render(more.ContextMenu);
                        var menuEncoder = new PngBitmapEncoder();
                        menuEncoder.Frames.Add(BitmapFrame.Create(menuBitmap));
                        using var menuStream = File.Create(Path.Combine(output, "MoreActionsMenu-100.png"));
                        menuEncoder.Save(menuStream);
                        foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                        {
                            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(620 * scale), (int)Math.Ceiling(490 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                            var background = new DrawingVisual();
                            using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 620, 490));
                            bitmap.Render(background);
                            bitmap.Render(root);
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var stream = File.Create(Path.Combine(output, $"MainWindow-narrow-{scale * 100:0}.png"));
                            encoder.Save(stream);
                        }
                        scroll.ScrollToVerticalOffset(300);
                        root.UpdateLayout();
                        var scrolled = new RenderTargetBitmap(620, 490, 96, 96, PixelFormats.Pbgra32);
                        var scrolledBackground = new DrawingVisual();
                        using (var drawing = scrolledBackground.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 620, 490));
                        scrolled.Render(scrolledBackground);
                        scrolled.Render(root);
                        var scrolledEncoder = new PngBitmapEncoder();
                        scrolledEncoder.Frames.Add(BitmapFrame.Create(scrolled));
                        using var scrolledStream = File.Create(Path.Combine(output, "MainWindow-narrow-scrolled-100.png"));
                        scrolledEncoder.Save(scrolledStream);
                        scroll.ScrollToEnd();
                        root.UpdateLayout();
                        Assert.InRange(results.TranslatePoint(new Point(), root).Y, 0, 490);
                        var bottom = new RenderTargetBitmap(620, 490, 96, 96, PixelFormats.Pbgra32);
                        bottom.Render(scrolledBackground);
                        bottom.Render(root);
                        var bottomEncoder = new PngBitmapEncoder();
                        bottomEncoder.Frames.Add(BitmapFrame.Create(bottom));
                        using var bottomStream = File.Create(Path.Combine(output, "MainWindow-narrow-bottom-100.png"));
                        bottomEncoder.Save(bottomStream);
                    }

                    Arrange(root, 1140, 780);
                    Assert.Equal(Visibility.Visible, vertical.Visibility);
                    Assert.InRange(settings.ActualWidth, adjustedSettingsWidth - 2, adjustedSettingsWidth + 2);
                    Assert.InRange(results.ActualHeight, adjustedResultsHeight - 3, adjustedResultsHeight + 3);
                    Assert.True(file.ActualWidth >= 520);
                }
                finally { window.Close(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Arrange(FrameworkElement root, double width, double height)
    {
        var size = new Size(width, height);
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
    }

    private static void Drag(Thumb splitter, double horizontal, double vertical)
    {
        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        splitter.RaiseEvent(new DragDeltaEventArgs(horizontal, vertical) { RoutedEvent = Thumb.DragDeltaEvent });
        splitter.RaiseEvent(new DragCompletedEventArgs(horizontal, vertical, false) { RoutedEvent = Thumb.DragCompletedEvent });
    }

    private static void DragInSteps(Thumb splitter, FrameworkElement root, double horizontal, double vertical, int count)
    {
        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        for (var i = 0; i < count; i++)
        {
            splitter.RaiseEvent(new DragDeltaEventArgs(horizontal, vertical) { RoutedEvent = Thumb.DragDeltaEvent });
            root.UpdateLayout();
        }
        splitter.RaiseEvent(new DragCompletedEventArgs(horizontal * count, vertical * count, false) { RoutedEvent = Thumb.DragCompletedEvent });
        root.UpdateLayout();
    }
}
