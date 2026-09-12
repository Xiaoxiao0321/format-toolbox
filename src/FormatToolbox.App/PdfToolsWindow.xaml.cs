using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FormatToolbox.Core;
using FormatToolbox.Infrastructure;

namespace FormatToolbox.App;

public partial class PdfToolsWindow : Window, INotifyPropertyChanged
{
    private readonly Action<ConversionRequest> _enqueue;
    private readonly PdfPreviewService _preview = new();
    private readonly PdfSplitService _split = new();
    private readonly string? _defaultOutputDirectory;
    private string _inputPath = "", _statusText = "请选择一个 PDF 文件。", _selectedSplitMode = "逐页拆分", _splitParameter = "2", _outputBaseName = "拆分结果";
    public ObservableCollection<PdfPageRow> Pages { get; } = [];
    public string[] SplitModes { get; } = ["逐页拆分", "指定范围", "每 N 页"];
    public string InputPath { get => _inputPath; set { _inputPath = value; OnChanged(); } }
    public string StatusText { get => _statusText; set { _statusText = value; OnChanged(); } }
    public string SelectedSplitMode { get => _selectedSplitMode; set { _selectedSplitMode = value; OnChanged(); } }
    public string SplitParameter { get => _splitParameter; set { _splitParameter = value; OnChanged(); } }
    public string OutputBaseName { get => _outputBaseName; set { _outputBaseName = value; OnChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public PdfToolsWindow(Action<ConversionRequest> enqueue, string? defaultOutputDirectory)
    {
        InitializeComponent(); DataContext = this; _enqueue = enqueue; _defaultOutputDirectory = defaultOutputDirectory;
    }

    private async void ChoosePdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PDF 文件|*.pdf" };
        if (dialog.ShowDialog(this) != true) return;
        InputPath = dialog.FileName; OutputBaseName = Path.GetFileNameWithoutExtension(InputPath); Pages.Clear();
        try
        {
            var count = _preview.GetPageCount(InputPath);
            for (var i = 0; i < count; i++)
            {
                StatusText = $"正在生成缩略图 {i + 1}/{count}…";
                var bytes = await _preview.RenderThumbnailAsync(InputPath, i);
                Pages.Add(new(i + 1, ToBitmap(bytes)));
            }
            StatusText = $"已加载 {count} 页。拖拽缩略图可以调整顺序。";
        }
        catch (Exception ex) { StatusText = "无法读取 PDF：" + ex.Message; }
    }

    private void SaveReordered_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(out var directory)) return;
        var order = Pages.Where(x => x.Keep).Select(x => x.OriginalPageNumber).ToArray();
        if (order.Length == 0) { StatusText = "至少保留一个页面。"; return; }
        _enqueue(new(InputPath, "pdf", directory, Options: new PdfOptions(PageOrder: order), OutputFileName: CleanName(OutputBaseName) + "-已整理"));
        StatusText = "页面整理任务已加入主窗口队列。";
    }

    private async void Split_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(out var directory)) return;
        try
        {
            IsEnabled = false; StatusText = "正在拆分 PDF…"; IReadOnlyList<string> outputs;
            if (SelectedSplitMode == "指定范围") outputs = await _split.SplitRangesAsync(InputPath, directory, CleanName(OutputBaseName), SplitParameter);
            else
            {
                var pages = SelectedSplitMode == "逐页拆分" ? 1 : int.TryParse(SplitParameter, out var value) && value > 0 ? value : throw new FormatException("每 N 页必须填写大于 0 的整数。");
                outputs = await _split.SplitEveryAsync(InputPath, directory, CleanName(OutputBaseName), pages);
            }
            StatusText = $"拆分完成，共生成 {outputs.Count} 个文件。";
        }
        catch (Exception ex) { StatusText = "拆分失败：" + ex.Message; }
        finally { IsEnabled = true; }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) { if (PageList.SelectedItem is PdfPageRow row) Move(row, Pages.IndexOf(row) - 1); }
    private void MoveDown_Click(object sender, RoutedEventArgs e) { if (PageList.SelectedItem is PdfPageRow row) Move(row, Pages.IndexOf(row) + 1); }
    private void KeepAll_Click(object sender, RoutedEventArgs e) { foreach (var page in Pages) page.Keep = true; }
    private void Invert_Click(object sender, RoutedEventArgs e) { foreach (var page in Pages) page.Keep = !page.Keep; }
    private void PageList_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && PageList.SelectedItem is PdfPageRow row) System.Windows.DragDrop.DoDragDrop(PageList, row, System.Windows.DragDropEffects.Move); }
    private void PageList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PdfPageRow)) is not PdfPageRow source) return;
        var element = PageList.InputHitTest(e.GetPosition(PageList)) as DependencyObject;
        while (element is not null && element is not ListBoxItem) element = VisualTreeHelper.GetParent(element);
        if (element is ListBoxItem item && item.DataContext is PdfPageRow target) Move(source, Pages.IndexOf(target));
    }
    private void Move(PdfPageRow row, int target) { var current = Pages.IndexOf(row); if (current < 0 || target < 0 || target >= Pages.Count || current == target) return; Pages.Move(current, target); PageList.SelectedItem = row; }
    private bool Ready(out string directory) { directory = _defaultOutputDirectory ?? (string.IsNullOrWhiteSpace(InputPath) ? "" : Path.Combine(Path.GetDirectoryName(InputPath)!, "转换结果")); if (!File.Exists(InputPath)) { StatusText = "请先选择有效的 PDF 文件。"; return false; } return true; }
    private static string CleanName(string value) => string.IsNullOrWhiteSpace(value) ? "PDF结果" : Path.GetFileNameWithoutExtension(value.Trim());
    private static BitmapImage ToBitmap(byte[] bytes) { var image = new BitmapImage(); using var stream = new MemoryStream(bytes); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image; }
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class PdfPageRow(int originalPageNumber, BitmapImage thumbnail) : INotifyPropertyChanged
{
    private bool _keep = true;
    public int OriginalPageNumber { get; } = originalPageNumber;
    public BitmapImage Thumbnail { get; } = thumbnail;
    public string Label => $"保留原第 {OriginalPageNumber} 页";
    public bool Keep { get => _keep; set { _keep = value; PropertyChanged?.Invoke(this, new(nameof(Keep))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
