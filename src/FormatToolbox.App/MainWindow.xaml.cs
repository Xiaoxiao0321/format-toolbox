using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using FormatToolbox.Core;
using FormatToolbox.Infrastructure;
using FormatToolbox.Infrastructure.Providers;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace FormatToolbox.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ConversionRegistry _registry;
    private readonly ConversionQueue _queue;
    private readonly HistoryStore _history = new();
    private readonly HashSet<Guid> _recordedTasks = [];
    private string _selectedTarget = "pdf", _outputDirectory = "", _statusText = "就绪";
    private string _quickGuideText = "选择上方功能即可切换到对应设置；也可以直接拖入文件，再从“目标格式”中选择输出。";
    private string _errorText = "";
    public string ErrorText { get => _errorText; private set { _errorText = value; OnChanged(); OnChanged(nameof(ErrorVisibility)); } }
    public Visibility ErrorVisibility => string.IsNullOrEmpty(ErrorText) ? Visibility.Collapsed : Visibility.Visible;
    private void ShowError(string message) { ErrorText = message; StatusText = message; }
    private void DismissError_Click(object sender, RoutedEventArgs e) => ErrorText = "";
    private string _imageQuality = "90", _renderDpi = "144", _pdfPageRange = "", _pdfWatermark = "", _ocrPageRange = "", _ocrDpi = "300", _compressionDpi = "144", _compressionQuality = "75", _mergeFileName = "";
    private string _selectedRotation = "0°", _selectedOcrLanguage = "中英混合";
    private bool _compressPdf = true, _rasterCompressPdf;
    public ObservableCollection<FileInfo> InputFiles { get; } = [];
    public ObservableCollection<QueueRow> QueueItems { get; } = [];
    public ObservableCollection<HistoryRow> HistoryItems { get; } = [];
    public string[] TargetFormats { get; } = ["pdf", "可搜索 PDF (OCR)", "png", "jpg", "bmp", "tiff"];
    public string[] RotationChoices { get; } = ["0°", "90°", "180°", "270°"];
    public string[] OcrLanguages { get; } = ["中英混合", "简体中文", "英文"];
    public string SelectedTarget { get => _selectedTarget; set { _selectedTarget = value; OnChanged(); OnChanged(nameof(ImageSettingsVisibility)); OnChanged(nameof(PdfSettingsVisibility)); OnChanged(nameof(OcrSettingsVisibility)); } }
    public string OutputDirectory { get => _outputDirectory; set { _outputDirectory = value; OnChanged(); } }
    public string StatusText { get => _statusText; set { _statusText = value; OnChanged(); } }
    public string QuickGuideText { get => _quickGuideText; set { _quickGuideText = value; OnChanged(); } }
    public string ImageQuality { get => _imageQuality; set { _imageQuality = value; OnChanged(); } }
    public string RenderDpi { get => _renderDpi; set { _renderDpi = value; OnChanged(); } }
    public string PdfPageRange { get => _pdfPageRange; set { _pdfPageRange = value; OnChanged(); } }
    public string PdfWatermark { get => _pdfWatermark; set { _pdfWatermark = value; OnChanged(); } }
    public string OcrPageRange { get => _ocrPageRange; set { _ocrPageRange = value; OnChanged(); } }
    public string OcrDpi { get => _ocrDpi; set { _ocrDpi = value; OnChanged(); } }
    public string SelectedRotation { get => _selectedRotation; set { _selectedRotation = value; OnChanged(); } }
    public string SelectedOcrLanguage { get => _selectedOcrLanguage; set { _selectedOcrLanguage = value; OnChanged(); } }
    public bool CompressPdf { get => _compressPdf; set { _compressPdf = value; OnChanged(); } }
    public bool RasterCompressPdf { get => _rasterCompressPdf; set { _rasterCompressPdf = value; OnChanged(); } }
    public string CompressionDpi { get => _compressionDpi; set { _compressionDpi = value; OnChanged(); } }
    public string CompressionQuality { get => _compressionQuality; set { _compressionQuality = value; OnChanged(); } }
    public string MergeFileName { get => _mergeFileName; set { _mergeFileName = value; OnChanged(); } }
    public Visibility ImageSettingsVisibility => SelectedTarget is "png" or "jpg" or "bmp" or "tiff" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PdfSettingsVisibility => SelectedTarget == "pdf" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OcrSettingsVisibility => SelectedTarget.StartsWith("可搜索", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent(); DataContext = this;
        var worker = Path.Combine(AppContext.BaseDirectory, "FormatToolbox.Worker.exe");
        IConversionProvider[] providers =
        [
            new ImageConversionProvider(),
            new PdfConversionProvider(),
            new PdfRenderProvider(),
            new OcrConversionProvider(Path.Combine(AppContext.BaseDirectory, "tessdata")),
            new FallbackConversionProvider("office.word", new WorkerConversionProvider("msoffice.word", ["doc", "docx", "rtf"], "Microsoft Word", "Word.Application", worker), new WorkerConversionProvider("wps.writer", ["doc", "docx", "rtf"], "WPS 文字", "kwps.application", worker), "Microsoft Word 或 WPS 文字"),
            new FallbackConversionProvider("office.excel", new WorkerConversionProvider("msoffice.excel", ["xls", "xlsx", "csv"], "Microsoft Excel", "Excel.Application", worker), new WorkerConversionProvider("wps.spreadsheet", ["xls", "xlsx", "csv"], "WPS 表格", "ket.application", worker), "Microsoft Excel 或 WPS 表格"),
            new FallbackConversionProvider("office.powerpoint", new WorkerConversionProvider("msoffice.powerpoint", ["ppt", "pptx"], "Microsoft PowerPoint", "PowerPoint.Application", worker), new WorkerConversionProvider("wps.presentation", ["ppt", "pptx"], "WPS 演示", "kwpp.application", worker), "Microsoft PowerPoint 或 WPS 演示"),
            new WorkerConversionProvider("autocad.dwg", ["dwg"], "AutoCAD", "AutoCAD.Application", worker)
        ];
        _registry = new(providers); _queue = new(_registry); _queue.Changed += QueueChanged;
        Loaded += async (_, _) => { await LoadHistoryAsync(); await DetectEnginesAsync(); };
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var files = paths.SelectMany(path => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories) : [path]);
        foreach (var path in files.Where(File.Exists)) if (!InputFiles.Any(x => x.FullName.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))) InputFiles.Add(new(path));
        StatusText = $"已添加 {InputFiles.Count} 个文件";
    }
    private void OnDrop(object sender, System.Windows.DragEventArgs e) { if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) AddPaths((string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)); }
    private void AddFiles_Click(object sender, RoutedEventArgs e) { var d = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "支持的文件|*.doc;*.docx;*.rtf;*.xls;*.xlsx;*.csv;*.ppt;*.pptx;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp;*.dwg|所有文件|*.*" }; if (d.ShowDialog() == true) AddPaths(d.FileNames); }
    private void Remove_Click(object sender, RoutedEventArgs e) { foreach (FileInfo item in InputList.SelectedItems.Cast<FileInfo>().ToArray()) InputFiles.Remove(item); }
    private void Clear_Click(object sender, RoutedEventArgs e) => InputFiles.Clear();
    private void MoveUp_Click(object sender, RoutedEventArgs e) { if (InputList.SelectedItem is FileInfo f) { var i = InputFiles.IndexOf(f); if (i > 0) InputFiles.Move(i, i - 1); } }
    private void MoveDown_Click(object sender, RoutedEventArgs e) { if (InputList.SelectedItem is FileInfo f) { var i = InputFiles.IndexOf(f); if (i >= 0 && i < InputFiles.Count - 1) InputFiles.Move(i, i + 1); } }
    private void Browse_Click(object sender, RoutedEventArgs e) { using var d = new WinForms.FolderBrowserDialog(); if (d.ShowDialog() == WinForms.DialogResult.OK) OutputDirectory = d.SelectedPath; }
    private void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        var action = (sender as FrameworkElement)?.Tag as string;
        switch (action)
        {
            case "office-pdf":
                SelectedTarget = "pdf";
                QuickGuideText = "文档转 PDF：添加 DOC/DOCX/RTF、XLS/XLSX/CSV 或 PPT/PPTX；程序优先使用 Microsoft Office，不可用时切换 WPS。";
                break;
            case "image":
                SelectedTarget = "jpg";
                QuickGuideText = "图片格式转换：添加 PNG/JPEG/BMP/TIFF/WebP，在“目标格式”选择 PNG、JPG、BMP 或 TIFF；JPG 可调整质量。";
                break;
            case "pdf-image":
                SelectedTarget = "png";
                QuickGuideText = "PDF 导出图片：添加 PDF，选择 PNG 或 JPG，并在图片设置中填写页码范围和渲染 DPI。";
                break;
            case "ocr":
                SelectedTarget = "可搜索 PDF (OCR)";
                QuickGuideText = "离线 OCR：添加 PDF 或图片，选择中英语言、页码范围和 DPI，输出带可搜索文字层的 PDF。";
                break;
            case "merge":
                SelectedTarget = "pdf";
                QuickGuideText = "合并为 PDF：添加至少两个 PDF/PNG/JPEG/BMP/TIFF，调整列表顺序，然后点击下方“合并为单个 PDF”。";
                break;
            case "pdf-tools":
                QuickGuideText = "PDF 页面工具：在弹出的窗口中预览页面、取消勾选以删除、调整顺序，或按页/范围/每 N 页拆分。";
                OpenPdfToolsWindow();
                break;
            case "dwg":
                SelectedTarget = "pdf";
                QuickGuideText = "DWG 转 PDF：需要完整版 AutoCAD。添加 DWG 后开始转换，将按布局顺序输出所有可打印布局。";
                break;
        }
        StatusText = QuickGuideText;
    }
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        ErrorText = "";
        if (InputFiles.Count == 0) { ShowError("请先添加文件。可点击“添加文件”或将文件拖入窗口。"); return; }
        if (!ConfirmRasterCompression()) return;
        foreach (var file in InputFiles) if (!Enqueue(file.FullName, SelectedTarget)) break;
    }
    private bool Enqueue(string path, string target, PdfOptions? mergeOptions = null)
    {
        if (!TryBuildOptions(path, target, out var options, out var error)) { ShowError(error); return false; }
        var ocr = target.StartsWith("可搜索", StringComparison.Ordinal);
        var request = new ConversionRequest(path, ocr ? "pdf" : target, string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory, Options: mergeOptions ?? options, OutputFileName: mergeOptions is null ? null : EmptyToNull(MergeFileName));
        if (_registry.Resolve(request) is null) { ShowError($"{Path.GetFileName(path)}：{_registry.DescribeUnsupportedConversion(request)}"); return false; }
        var item = _queue.Enqueue(request);
        QueueItems.Add(new(item)); StatusText = "任务已加入队列。"; return true;
    }
    private void MergePdf_Click(object sender, RoutedEventArgs e)
    {
        var selected = InputList.SelectedItems.Cast<FileInfo>().Select(x => x.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = InputFiles.Where(x => selected.Count == 0 || selected.Contains(x.FullName)).ToArray();
        var supported = new HashSet<string>([".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"], StringComparer.OrdinalIgnoreCase);
        if (files.Length < 2) { StatusText = "合并至少需要两个文件；请选择文件或保留两个以上输入项。"; return; }
        if (files.Any(x => !supported.Contains(x.Extension))) { StatusText = "合并仅支持 PDF 和常见图片格式。"; return; }
        if (!ConfirmRasterCompression()) return;
        if (!TryCreatePdfOptions(files.Skip(1).Select(x => x.FullName).ToArray(), out PdfOptions options, out var error)) { StatusText = error; return; }
        Enqueue(files[0].FullName, "pdf", options);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { foreach (QueueRow row in QueueList.SelectedItems) _queue.Cancel(row.Item.Id); }
    private void Retry_Click(object sender, RoutedEventArgs e) { foreach (var row in QueueItems.Where(x => x.Item.Status == ConversionStatus.Failed).ToArray()) { var displayTarget = row.Item.Request.Options is OcrOptions ? "可搜索 PDF (OCR)" : row.Item.Request.TargetFormat; var item = _queue.Enqueue(row.Item.Request); QueueItems.Add(new(item)); StatusText = $"已重试 {displayTarget} 任务。"; } }
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = (QueueList.SelectedItem as QueueRow)?.Item;
            var output = selected?.Result?.OutputFiles.FirstOrDefault()
                ?? QueueItems.LastOrDefault(x => x.Item.Status == ConversionStatus.Succeeded)?.Item.Result?.OutputFiles.FirstOrDefault();
            var path = output is not null ? Path.GetDirectoryName(output)
                : !string.IsNullOrWhiteSpace(OutputDirectory) ? Path.GetFullPath(OutputDirectory)
                : InputFiles.FirstOrDefault() is { } input ? Path.Combine(input.DirectoryName!, "转换结果") : null;
            if (path is null) { ShowError("请先添加文件或选择输出目录。转换完成后，可在这里打开结果目录。"); return; }
            if (!Directory.Exists(path)) { ShowError($"输出目录尚不存在：{path}。请先完成转换，或检查目录是否已移动或删除。"); return; }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            StatusText = $"已打开输出目录：{path}";
        }
        catch (Exception ex) { ShowError("无法打开输出目录：" + ex.Message); }
    }
    private void OpenLogs_Click(object sender, RoutedEventArgs e) { Directory.CreateDirectory(DiagnosticLog.DirectoryPath); Process.Start(new ProcessStartInfo(DiagnosticLog.DirectoryPath) { UseShellExecute = true }); }
    private void OpenPdfTools_Click(object sender, RoutedEventArgs e)
        => OpenPdfToolsWindow();
    private void OpenPdfToolsWindow()
    {
        var window = new PdfToolsWindow(request => { var item = _queue.Enqueue(request); QueueItems.Add(new(item)); }, string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory) { Owner = this };
        window.ShowDialog();
    }
    private async void Detect_Click(object sender, RoutedEventArgs e) => await DetectEnginesAsync();
    private async Task DetectEnginesAsync() { var results = new List<string>(); foreach (var p in _registry.Providers) { var r = await p.CheckAvailabilityAsync(); results.Add($"{p.Id}: {(r.IsAvailable ? "可用" + (string.IsNullOrWhiteSpace(r.Version) ? "" : $" ({r.Version})") : r.Reason)}"); } var runtime = RuntimeDependencyDetector.DetectVcppX64(); results.Add("VC++ x64: " + (runtime.IsAvailable ? "可用" : runtime.DisplayText)); StatusText = string.Join("；", results); }
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
    private void Feedback_Click(object sender, RoutedEventArgs e) => new FeedbackWindow(_registry.Providers) { Owner = this }.ShowDialog();
    private bool TryBuildOptions(string path, string target, out ConversionOptions? options, out string error)
    {
        options = null; error = "";
        if (target.StartsWith("可搜索", StringComparison.Ordinal))
        {
            if (!TryInt(OcrDpi, 72, 600, "OCR DPI", out var dpi, out error)) return false;
            var languages = SelectedOcrLanguage switch { "简体中文" => "chi_sim", "英文" => "eng", _ => "chi_sim+eng" };
            options = new OcrOptions(languages, EmptyToNull(OcrPageRange), dpi); return true;
        }
        if (target == "pdf") { var ok = TryCreatePdfOptions(null, out PdfOptions pdfOptions, out error); options = pdfOptions; return ok; }
        if (!TryInt(ImageQuality, 1, 100, "图片质量", out var quality, out error) || !TryInt(RenderDpi, 72, 600, "渲染 DPI", out var dpiValue, out error)) return false;
        options = Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? new PdfRenderOptions(EmptyToNull(PdfPageRange), dpiValue, quality)
            : new ImageOptions(quality, dpiValue);
        return true;
    }
    private bool TryCreatePdfOptions(IReadOnlyList<string>? additional, out PdfOptions options, out string error)
    {
        options = new PdfOptions();
        if (!TryInt(CompressionDpi, 72, 300, "强力压缩 DPI", out var dpi, out error) || !TryInt(CompressionQuality, 20, 95, "JPEG 质量", out var quality, out error)) return false;
        options = new PdfOptions(EmptyToNull(PdfPageRange), CompressPdf ? 6 : 0, EmptyToNull(PdfWatermark), ParseRotation(), additional, RasterCompressPdf, dpi, quality);
        return true;
    }
    private int ParseRotation() => int.TryParse(SelectedRotation.TrimEnd('°'), out var value) ? value : 0;
    private bool ConfirmRasterCompression() => !RasterCompressPdf || System.Windows.MessageBox.Show("强力压缩会把页面栅格化，原有可选文字、链接、批注和表单将无法保留。确定继续吗？", "确认强力压缩", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool TryInt(string text, int min, int max, string name, out int value, out string error)
    {
        if (int.TryParse(text, out value) && value >= min && value <= max) { error = ""; return true; }
        error = $"{name} 必须是 {min}–{max} 之间的整数。"; return false;
    }
    private void QueueChanged(object? sender, QueueItem item) => Dispatcher.Invoke(async () =>
    {
        var row = QueueItems.FirstOrDefault(x => x.Item.Id == item.Id); row?.Refresh();
        if (item.Result is not null && _recordedTasks.Add(item.Id))
        {
            if (item.Status == ConversionStatus.Failed) ShowError($"{Path.GetFileName(item.Request.InputPath)}：{item.Result.ErrorMessage} 详情可在“任务队列”中查看。");
            var entry = new HistoryEntry(DateTimeOffset.Now, item.Request.InputPath, item.Request.TargetFormat, item.Status, item.Result.ErrorMessage, item.Result.OutputFiles);
            await _history.AppendAsync(entry); HistoryItems.Insert(0, new(entry));
        }
    });
    private async Task LoadHistoryAsync()
    {
        HistoryItems.Clear(); foreach (var entry in (await _history.LoadAsync()).Reverse()) HistoryItems.Add(new(entry));
    }
    private void OpenHistoryResult_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryRow row || row.Entry.OutputFiles?.FirstOrDefault(File.Exists) is not string path) { StatusText = "所选记录没有可用的输出文件。"; return; }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    private void ReAddHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryRow row || !File.Exists(row.Entry.InputPath)) { StatusText = "原输入文件已不存在。"; return; }
        if (!InputFiles.Any(x => x.FullName.Equals(row.Entry.InputPath, StringComparison.OrdinalIgnoreCase))) InputFiles.Add(new(row.Entry.InputPath));
        SelectedTarget = row.Entry.TargetFormat; StatusText = "历史任务已重新添加到输入列表。";
    }
    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show("确定清空全部转换历史吗？此操作不会删除输出文件。", "清空历史", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _history.Clear(); HistoryItems.Clear(); StatusText = "历史记录已清空。";
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_queue.HasActiveItems) return;
        if (System.Windows.MessageBox.Show("仍有转换任务正在运行。确定退出并取消这些任务吗？", "任务仍在运行", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No) e.Cancel = true;
        else foreach (var row in QueueItems.Where(x => x.Item.Status is ConversionStatus.Waiting or ConversionStatus.Checking or ConversionStatus.Processing)) _queue.Cancel(row.Item.Id);
    }
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class HistoryRow(HistoryEntry entry)
{
    public HistoryEntry Entry { get; } = entry;
    public string TimeText => Entry.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string FileName => Path.GetFileName(Entry.InputPath);
    public string Target => Entry.TargetFormat.ToUpperInvariant();
    public string StatusText => Entry.Status == ConversionStatus.Succeeded ? "成功" : Entry.Status == ConversionStatus.Cancelled ? "已取消" : "失败";
    public string Detail => Entry.OutputFiles?.Count > 0 ? string.Join("；", Entry.OutputFiles) : Entry.Error ?? "";
}

public sealed class QueueRow(QueueItem item) : INotifyPropertyChanged
{
    public QueueItem Item { get; } = item;
    public string FileName => Path.GetFileName(Item.Request.InputPath);
    public string Target => Item.Request.TargetFormat.ToUpperInvariant();
    public string StatusText => Item.Status switch { ConversionStatus.Waiting => "等待", ConversionStatus.Checking => "检测", ConversionStatus.Processing => "处理中", ConversionStatus.Succeeded => "成功", ConversionStatus.Failed => "失败", _ => "已取消" };
    public string ProgressText => $"{Item.Progress:0}%";
    public string Message => Item.Message;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() { foreach (var name in new[] { nameof(StatusText), nameof(ProgressText), nameof(Message) }) PropertyChanged?.Invoke(this, new(name)); }
}
