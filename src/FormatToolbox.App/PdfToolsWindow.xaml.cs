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
    private CancellationTokenSource? _previewCancellation;
    private readonly List<Task> _previewTasks = [];
    private bool _closingPreview, _allowClose;
    private bool _previewReady;
    public bool PreviewReady { get => _previewReady; private set { _previewReady = value; OnChanged(); } }
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
        Closing += OnClosing;
        WindowSizing.Attach(this);
    }

    private async void ChoosePdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PDF 文件|*.pdf" };
        if (dialog.ShowDialog(this) != true) return;
        _previewCancellation?.Cancel();
        InputPath = dialog.FileName; OutputBaseName = Path.GetFileNameWithoutExtension(InputPath); Pages.Clear();
        PreviewReady = false;
        var cancellation = new CancellationTokenSource(); _previewCancellation = cancellation;
        _previewTasks.RemoveAll(x => x.IsCompleted);
        var task = LoadPreviewAsync(InputPath, cancellation); _previewTasks.Add(task);
        await task;
    }

    private async Task LoadPreviewAsync(string path, CancellationTokenSource cancellation)
    {
        try
        {
            var token = cancellation.Token;
            var count = await _preview.GetPageCountAsync(path, token);
            for (var i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                StatusText = $"正在生成缩略图 {i + 1}/{count}…";
                var bytes = await _preview.RenderThumbnailAsync(path, i, token);
                token.ThrowIfCancellationRequested();
                Pages.Add(new(i + 1, ToBitmap(bytes)));
            }
            PreviewReady = true;
            StatusText = $"已加载 {count} 页。拖拽缩略图可以调整顺序。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (_previewCancellation == cancellation) StatusText = "无法读取 PDF：" + ex.Message; }
        finally { if (_previewCancellation == cancellation) _previewCancellation = null; cancellation.Dispose(); }
    }

    private void SaveReordered_Click(object sender, RoutedEventArgs e)
    {
        if (!PreviewReady) { StatusText = "请等待页面预览加载完成。"; return; }
        if (!Ready(out var directory)) return;
        var order = Pages.Where(x => x.Keep).Select(x => x.OriginalPageNumber).ToArray();
        if (order.Length == 0) { StatusText = "至少保留一个页面。"; return; }
        _enqueue(new(InputPath, "pdf", directory, Options: new PdfOptions(PageOrder: order), OutputFileName: CleanName(OutputBaseName) + "-已整理"));
        StatusText = "页面整理任务已加入主窗口队列。";
    }

    private void Split_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(out var directory)) return;
        try
        {
            var pages = SelectedSplitMode is "逐页拆分" or "指定范围" ? 1 : int.TryParse(SplitParameter, out var value) && value > 0 ? value : throw new FormatException("每 N 页必须填写大于 0 的整数。");
            if (SelectedSplitMode == "指定范围" && string.IsNullOrWhiteSpace(SplitParameter)) throw new FormatException("请填写拆分页码范围，例如 1-3;4-6。");
            _enqueue(new(InputPath, "pdf", directory, Options: new PdfSplitOptions(pages, SelectedSplitMode == "指定范围" ? SplitParameter : null), OutputFileName: CleanName(OutputBaseName)));
            StatusText = "拆分任务已加入主窗口队列；可关闭此窗口查看进度、取消或重试。";
        }
        catch (Exception ex) { StatusText = "拆分失败：" + ex.Message; }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closingPreview) return;
        _closingPreview = true; IsEnabled = false; _previewCancellation?.Cancel();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        await Task.WhenAll(_previewTasks);
        _allowClose = true; Close();
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
