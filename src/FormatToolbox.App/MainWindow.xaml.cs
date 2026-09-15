using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using FormatToolbox.Core;
using FormatToolbox.Infrastructure;
using FormatToolbox.Infrastructure.Providers;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace FormatToolbox.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private void Content_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source != null)
        {
            if (source is System.Windows.Controls.ComboBox) return;
            if (source is ScrollViewer viewer &&
                viewer.ScrollableHeight > 0 &&
                (e.Delta > 0 ? viewer.VerticalOffset > 0 : viewer.VerticalOffset < viewer.ScrollableHeight))
            {
                var lines = SystemParameters.WheelScrollLines;
                if (lines == 0) return;
                var distance = lines < 0 ? viewer.ViewportHeight : lines * 16.0;
                viewer.ScrollToVerticalOffset(viewer.VerticalOffset - e.Delta / 120.0 * distance);
                e.Handled = true;
                return;
            }
            source = source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
    }
    private readonly ConversionRegistry _registry;
    private readonly ConversionQueue _queue;
    private readonly HistoryStore _history;
    private readonly HashSet<Guid> _recordedTasks = [];
    private readonly object _historyGate = new();
    private readonly List<Task> _historyWrites = [];
    private readonly List<HistoryEntry> _unsavedHistory = [];
    private bool _exitInProgress, _allowClose;
    private string _selectedTarget = "pdf", _outputDirectory = "", _statusText = "就绪";
    private string _quickGuideText = "选择顶部功能后在此调整参数；也可以先拖入文件，再选择目标格式。";
    private string _errorText = "";
    public string ErrorText { get => _errorText; private set { _errorText = value; OnChanged(); OnChanged(nameof(ErrorVisibility)); } }
    public Visibility ErrorVisibility => string.IsNullOrEmpty(ErrorText) ? Visibility.Collapsed : Visibility.Visible;
    private void ShowError(string message) { ErrorText = message; StatusText = message; }
    private void DismissError_Click(object sender, RoutedEventArgs e) => ErrorText = "";
    private string _imageQuality = "90", _renderDpi = "144", _pdfPageRange = "", _pdfWatermark = "", _ocrPageRange = "", _ocrDpi = "300", _compressionDpi = "144", _compressionQuality = "75", _mergeFileName = "";
    private string _selectedRotation = "0°", _selectedOcrLanguage = "中英混合";
    private string? _quickAction;
    private bool _isNarrowLayout;
    private double _savedSettingsWidth = 320;
    private GridLength _wideWorkLength = new(6.4, GridUnitType.Star), _wideResultsLength = new(3.6, GridUnitType.Star);
    private double _dragWorkHeight, _dragTotalHeight;
    private bool _compressPdf = true, _rasterCompressPdf;
    public bool OcrGrayscale { get; set; }
    public ObservableCollection<FileInfo> InputFiles { get; } = [];
    public ObservableCollection<QueueRow> QueueItems { get; } = [];
    public ObservableCollection<HistoryRow> HistoryItems { get; } = [];
    public string[] TargetFormats { get; } = ["pdf", "可搜索 PDF (OCR)", "png", "jpg", "bmp", "tiff"];
    public string[] RotationChoices { get; } = ["0°", "90°", "180°", "270°"];
    public string[] OcrLanguages { get; } = ["中英混合", "简体中文", "英文"];
    public string SelectedTarget { get => _selectedTarget; set { if (_selectedTarget != value) AdvancedSettingsExpander.IsExpanded = false; _selectedTarget = value; OnChanged(); NotifySettingsChanged(); } }
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
    public bool RasterCompressPdf { get => _rasterCompressPdf; set { _rasterCompressPdf = value; OnChanged(); NotifySettingsChanged(); } }
    public string CompressionDpi { get => _compressionDpi; set { _compressionDpi = value; OnChanged(); } }
    public string CompressionQuality { get => _compressionQuality; set { _compressionQuality = value; OnChanged(); } }
    public string MergeFileName { get => _mergeFileName; set { _mergeFileName = value; OnChanged(); } }
    public Visibility ImageSettingsVisibility => SelectedTarget is "png" or "jpg" or "bmp" or "tiff" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility JpegQualityVisibility => SelectedTarget == "jpg" ? Visibility.Visible : Visibility.Collapsed;
    private bool HasPdfInput => InputFiles.Any(x => x.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) || InputFiles.Count == 0 && _quickAction is "pdf-image" or "ocr";
    private static bool UsesPdfSettings(string path) => new[] { ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public Visibility PdfRenderSettingsVisibility => SelectedTarget is "png" or "jpg" && HasPdfInput ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PdfInputSettingsVisibility => HasPdfInput ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PdfSettingsVisibility => SelectedTarget == "pdf" && (InputFiles.Any(x => UsesPdfSettings(x.FullName)) || InputFiles.Count == 0 && _quickAction is not ("office-pdf" or "dwg")) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CompressionSettingsVisibility => RasterCompressPdf ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DwgWarningVisibility => SelectedTarget == "pdf" && (_quickAction == "dwg" || InputFiles.Any(x => x.Extension.Equals(".dwg", StringComparison.OrdinalIgnoreCase))) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OcrSettingsVisibility => SelectedTarget.StartsWith("可搜索", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void NotifySettingsChanged()
    {
        foreach (var name in new[] { nameof(ImageSettingsVisibility), nameof(JpegQualityVisibility), nameof(PdfRenderSettingsVisibility), nameof(PdfInputSettingsVisibility), nameof(PdfSettingsVisibility), nameof(CompressionSettingsVisibility), nameof(OcrSettingsVisibility), nameof(DwgWarningVisibility) }) OnChanged(name);
    }

    public MainWindow() : this(new HistoryStore()) { }

    public MainWindow(HistoryStore history)
    {
        _history = history;
        InitializeComponent(); DataContext = this;
        InputFiles.CollectionChanged += (_, _) => NotifySettingsChanged();
        SourceInitialized += (_, _) => FitWindowToScreen();
        var worker = Path.Combine(AppContext.BaseDirectory, "FormatToolbox.Worker.exe");
        IConversionProvider[] providers =
        [
            new ImageConversionProvider(),
            new PdfConversionProvider(),
            new PdfSplitProvider(),
            new PdfRenderProvider(),
            new OcrConversionProvider(Path.Combine(AppContext.BaseDirectory, "tessdata")),
            new FallbackConversionProvider("office.word", new WorkerConversionProvider("msoffice.word", ["doc", "docx", "rtf"], "Microsoft Word", "Word.Application", worker), new WorkerConversionProvider("wps.writer", ["doc", "docx", "rtf"], "WPS 文字", "kwps.application", worker), "Microsoft Word 或 WPS 文字"),
            new FallbackConversionProvider("office.excel", new WorkerConversionProvider("msoffice.excel", ["xls", "xlsx", "csv"], "Microsoft Excel", "Excel.Application", worker), new WorkerConversionProvider("wps.spreadsheet", ["xls", "xlsx", "csv"], "WPS 表格", "ket.application", worker), "Microsoft Excel 或 WPS 表格"),
            new FallbackConversionProvider("office.powerpoint", new WorkerConversionProvider("msoffice.powerpoint", ["ppt", "pptx"], "Microsoft PowerPoint", "PowerPoint.Application", worker), new WorkerConversionProvider("wps.presentation", ["ppt", "pptx"], "WPS 演示", "kwpp.application", worker), "Microsoft PowerPoint 或 WPS 演示"),
            new WorkerConversionProvider("autocad.dwg", ["dwg"], "AutoCAD", "AutoCAD.Application", worker)
        ];
        _registry = new(providers); _queue = new(_registry); _queue.Changed += QueueChanged;
        Loaded += async (_, _) => { DiagnosticLog.WriteEnvironment(); await LoadHistoryAsync(); await DetectEnginesAsync(); };
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var files = paths.SelectMany(path => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories) : [path]);
        foreach (var path in files.Where(File.Exists)) if (!InputFiles.Any(x => x.FullName.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))) InputFiles.Add(new(path));
        StatusText = $"已添加 {InputFiles.Count} 个文件";
    }
    private void OnDrop(object sender, System.Windows.DragEventArgs e) { if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) AddPaths((string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)); }
    private void AddFiles_Click(object sender, RoutedEventArgs e) { var d = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "支持的文件|*.pdf;*.doc;*.docx;*.rtf;*.xls;*.xlsx;*.csv;*.ppt;*.pptx;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp;*.dwg|PDF 文件|*.pdf|所有文件|*.*" }; if (d.ShowDialog() == true) AddPaths(d.FileNames); }
    private void Remove_Click(object sender, RoutedEventArgs e) { foreach (FileInfo item in InputList.SelectedItems.Cast<FileInfo>().ToArray()) InputFiles.Remove(item); }
    private void Clear_Click(object sender, RoutedEventArgs e) => InputFiles.Clear();
    private void MoveUp_Click(object sender, RoutedEventArgs e) { if (InputList.SelectedItem is FileInfo f) { var i = InputFiles.IndexOf(f); if (i > 0) InputFiles.Move(i, i - 1); } }
    private void MoveDown_Click(object sender, RoutedEventArgs e) { if (InputList.SelectedItem is FileInfo f) { var i = InputFiles.IndexOf(f); if (i >= 0 && i < InputFiles.Count - 1) InputFiles.Move(i, i + 1); } }
    private void Browse_Click(object sender, RoutedEventArgs e) { using var d = new WinForms.FolderBrowserDialog(); if (d.ShowDialog() == WinForms.DialogResult.OK) OutputDirectory = d.SelectedPath; }
    private void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        var action = (sender as FrameworkElement)?.Tag as string;
        if (_quickAction != action) AdvancedSettingsExpander.IsExpanded = false;
        _quickAction = action;
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
        NotifySettingsChanged();
    }
    private void MoreActions_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = MoreActionsButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
    private void MainContentScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMainLayout();
    private void WorkSplitter_DragStarted(object sender, DragStartedEventArgs e)
        => _savedSettingsWidth = Math.Clamp(SettingsColumn.ActualWidth, 270, 420);
    private void WorkSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isNarrowLayout) return;
        var maximum = Math.Max(270, Math.Min(420, WorkLayout.ActualWidth - FilesColumn.MinWidth - SettingsSplitterColumn.ActualWidth));
        _savedSettingsWidth = Math.Clamp(_savedSettingsWidth - e.HorizontalChange, 270, maximum);
        SettingsColumn.Width = new GridLength(_savedSettingsWidth);
        e.Handled = true;
    }
    private void ResultsSplitter_DragStarted(object sender, DragStartedEventArgs e)
    {
        _dragWorkHeight = WorkRow.ActualHeight;
        _dragTotalHeight = WorkRow.ActualHeight + ResultsRow.ActualHeight;
    }
    private void ResultsSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_dragTotalHeight <= 0) ResultsSplitter_DragStarted(sender, new DragStartedEventArgs(0, 0));
        var minimumResults = ResultsRow.MinHeight;
        if (_dragTotalHeight < WorkRow.MinHeight + minimumResults) return;
        _dragWorkHeight = Math.Clamp(_dragWorkHeight + e.VerticalChange, WorkRow.MinHeight, _dragTotalHeight - minimumResults);
        WorkRow.Height = new GridLength(_dragWorkHeight, GridUnitType.Star);
        ResultsRow.Height = new GridLength(_dragTotalHeight - _dragWorkHeight, GridUnitType.Star);
        e.Handled = true;
    }
    private void UpdateMainLayout()
    {
        if (MainLayout is null || WorkLayout is null) return;
        // ViewportWidth can still describe the previous measure during SizeChanged.
        var viewportWidth = MainContentScroll.ActualWidth - SystemParameters.VerticalScrollBarWidth;
        var viewportHeight = MainContentScroll.ActualHeight - SystemParameters.HorizontalScrollBarHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;
        var narrow = MainContentScroll.ActualWidth < 980;
        if (narrow != _isNarrowLayout)
        {
            if (narrow)
            {
                _wideWorkLength = WorkRow.Height; _wideResultsLength = ResultsRow.Height;
                WorkRow.Height = new GridLength(7, GridUnitType.Star);
                ResultsRow.Height = new GridLength(3, GridUnitType.Star);
                ResultsRow.MinHeight = 215;
                if (SettingsColumn.ActualWidth >= 270) _savedSettingsWidth = Math.Clamp(SettingsColumn.ActualWidth, 270, 420);
                SettingsColumn.MinWidth = 0; SettingsColumn.MaxWidth = double.PositiveInfinity; SettingsColumn.Width = new GridLength(0);
                SettingsSplitterColumn.Width = new GridLength(0);
                FilesColumn.MinWidth = 0; FileGroup.MinWidth = 0;
                SettingsPanel.MinWidth = 0; SettingsPanel.MaxWidth = double.PositiveInfinity;
                Grid.SetColumn(SettingsPanel, 0); Grid.SetRow(SettingsPanel, 0); Grid.SetRowSpan(SettingsPanel, 1);
                Grid.SetRow(FileGroup, 2); Grid.SetRowSpan(FileGroup, 1); FileGroup.Margin = new Thickness(0);
                WorkSplitter.Visibility = Visibility.Collapsed;
                SettingsStackRow.Height = new GridLength(285);
                NarrowGapRow.Height = new GridLength(8);
            }
            else
            {
                WorkRow.Height = _wideWorkLength; ResultsRow.Height = _wideResultsLength;
                ResultsRow.MinHeight = 230;
                SettingsStackRow.Height = new GridLength(0); NarrowGapRow.Height = new GridLength(0);
                SettingsColumn.MinWidth = 270; SettingsColumn.MaxWidth = 420; SettingsColumn.Width = new GridLength(_savedSettingsWidth);
                SettingsSplitterColumn.Width = new GridLength(8);
                FilesColumn.MinWidth = 520; FileGroup.MinWidth = 520;
                SettingsPanel.MinWidth = 270; SettingsPanel.MaxWidth = 420;
                Grid.SetColumn(SettingsPanel, 2); Grid.SetRow(SettingsPanel, 0); Grid.SetRowSpan(SettingsPanel, 3);
                Grid.SetRow(FileGroup, 0); Grid.SetRowSpan(FileGroup, 3); FileGroup.Margin = new Thickness(0, 0, 6, 0);
                WorkSplitter.Visibility = Visibility.Visible;
            }
            _isNarrowLayout = narrow;
        }
        MainLayout.MinHeight = narrow ? 1000 : 610;
        MainLayout.Width = Math.Max(560, viewportWidth);
        MainLayout.Height = Math.Max(MainLayout.MinHeight, viewportHeight);
    }
    private void InputList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (InputPathColumn is not null) InputPathColumn.Width = Math.Max(180, e.NewSize.Width - 315);
    }
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        ErrorText = "";
        if (InputFiles.Count == 0) { ShowError("请先添加文件。可点击“添加文件”或将文件拖入窗口。"); return; }
        if (!ConfirmRasterCompression(SelectedTarget == "pdf" && InputFiles.Any(x => x.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)))) return;
        foreach (var file in InputFiles) if (!Enqueue(file.FullName, SelectedTarget)) break;
    }
    private bool Enqueue(string path, string target, PdfOptions? mergeOptions = null)
    {
        if (!TryBuildOptions(path, target, out var options, out var error)) { ShowError(error); return false; }
        var ocr = target.StartsWith("可搜索", StringComparison.Ordinal);
        var request = new ConversionRequest(path, ocr ? "pdf" : target, string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory, Options: mergeOptions ?? options, OutputFileName: mergeOptions is null ? null : EmptyToNull(MergeFileName));
        if (_registry.Resolve(request) is null) { ShowError($"{Path.GetFileName(path)}：{_registry.DescribeUnsupportedConversion(request)}"); return false; }
        AddQueueRequest(request); ResultsTabs.BringIntoView(); StatusText = "任务已加入队列。"; return true;
    }
    private void MergePdf_Click(object sender, RoutedEventArgs e)
    {
        var selected = InputList.SelectedItems.Cast<FileInfo>().Select(x => x.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = InputFiles.Where(x => selected.Count == 0 || selected.Contains(x.FullName)).ToArray();
        var supported = new HashSet<string>([".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp"], StringComparer.OrdinalIgnoreCase);
        if (files.Length < 2) { StatusText = "合并至少需要两个文件；请选择文件或保留两个以上输入项。"; return; }
        if (files.Any(x => !supported.Contains(x.Extension))) { StatusText = "合并仅支持 PDF 和常见图片格式。"; return; }
        if (!ConfirmRasterCompression(files.Any(x => x.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)))) return;
        if (!TryCreatePdfOptions(files.Skip(1).Select(x => x.FullName).ToArray(), out PdfOptions options, out var error, files.Any(x => x.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)))) { StatusText = error; return; }
        Enqueue(files[0].FullName, "pdf", options);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { foreach (QueueRow row in QueueList.SelectedItems) _queue.Cancel(row.Item.Id); }
    private void Retry_Click(object sender, RoutedEventArgs e) { foreach (var row in QueueItems.Where(x => x.Item.Status is ConversionStatus.Failed or ConversionStatus.PartialSucceeded).ToArray()) { var item = AddQueueRequest(row.Item.Request); StatusText = $"已重试 {ConversionLabels.Target(item.Request)} 任务；已有结果将按原任务的重名策略处理。"; } }
    private QueueItem AddQueueRequest(ConversionRequest request)
    {
        var item = _queue.Enqueue(request);
        QueueItems.Add(new(item)); ResultsTabs.SelectedIndex = 0;
        return item;
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = (QueueList.SelectedItem as QueueRow)?.Item;
            var output = selected?.Result?.OutputFiles.FirstOrDefault()
                ?? QueueItems.LastOrDefault(x => x.Item.Result?.OutputFiles.Count > 0)?.Item.Result?.OutputFiles.FirstOrDefault();
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

    private void FitWindowToScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var screen = WinForms.Screen.FromHandle(handle).WorkingArea;
        var source = HwndSource.FromHwnd(handle);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var area = Rect.Transform(new Rect(screen.X, screen.Y, screen.Width, screen.Height), transform);
        var availableWidth = Math.Max(1, area.Width - 24);
        var availableHeight = Math.Max(1, area.Height - 24);
        MinWidth = Math.Min(640, availableWidth);
        MinHeight = Math.Min(420, availableHeight);
        Width = Math.Min(1180, availableWidth);
        Height = Math.Min(860, availableHeight);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
        UpdateMainLayout();
    }
    private void OpenLogs_Click(object sender, RoutedEventArgs e) { Directory.CreateDirectory(DiagnosticLog.DirectoryPath); Process.Start(new ProcessStartInfo(DiagnosticLog.DirectoryPath) { UseShellExecute = true }); }
    private void OpenPdfTools_Click(object sender, RoutedEventArgs e)
        => OpenPdfToolsWindow();
    private void OpenPdfToolsWindow()
    {
        var window = new PdfToolsWindow(request => AddQueueRequest(request), string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory) { Owner = this };
        window.ShowDialog();
    }
    private async void Detect_Click(object sender, RoutedEventArgs e) => await DetectEnginesAsync();
    private async Task DetectEnginesAsync() { var results = new List<string>(); foreach (var p in _registry.Providers) { var r = await p.CheckAvailabilityAsync(); DiagnosticLog.RecordEngine(p.Id, r); results.Add($"{p.Id}: {(r.IsAvailable ? "可用" + (string.IsNullOrWhiteSpace(r.Version) ? "" : $" ({r.Version})") : r.Reason)}"); } var runtime = RuntimeDependencyDetector.DetectVcppX64(); results.Add("VC++ x64: " + (runtime.IsAvailable ? "可用" : runtime.DisplayText)); StatusText = string.Join("；", results); }
    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try { System.Windows.Clipboard.SetText(DiagnosticLog.GetInformation()); StatusText = "诊断信息已复制，可粘贴到反馈中。"; }
        catch (Exception ex) { DiagnosticLog.Write("diagnostics.clipboard", ex); ShowError("剪贴板暂时不可用，请稍后重试。"); }
    }
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
    private void Feedback_Click(object sender, RoutedEventArgs e) => new FeedbackWindow(_registry.Providers) { Owner = this }.ShowDialog();
    private bool TryBuildOptions(string path, string target, out ConversionOptions? options, out string error)
    {
        options = null; error = "";
        if (target.StartsWith("可搜索", StringComparison.Ordinal))
        {
            if (!TryInt(OcrDpi, 72, 600, "OCR DPI", out var dpi, out error)) return false;
            var languages = SelectedOcrLanguage switch { "简体中文" => "chi_sim", "英文" => "eng", _ => "chi_sim+eng" };
            options = new OcrOptions(languages, EmptyToNull(OcrPageRange), dpi, OcrGrayscale); return true;
        }
        if (target == "pdf")
        {
            if (!UsesPdfSettings(path)) return true;
            var ok = TryCreatePdfOptions(null, out PdfOptions pdfOptions, out error, Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)); options = pdfOptions; return ok;
        }
        var quality = 90;
        if (target is "jpg" or "jpeg" && !TryInt(ImageQuality, 1, 100, "图片质量", out quality, out error)) return false;
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryInt(RenderDpi, 72, 600, "渲染 DPI", out var dpiValue, out error)) return false;
            options = new PdfRenderOptions(EmptyToNull(PdfPageRange), dpiValue, quality);
        }
        else options = new ImageOptions(quality);
        return true;
    }
    private bool TryCreatePdfOptions(IReadOnlyList<string>? additional, out PdfOptions options, out string error, bool hasPdf)
    {
        options = new PdfOptions();
        error = "";
        var dpi = 144; var quality = 75;
        if (hasPdf && RasterCompressPdf && (!TryInt(CompressionDpi, 72, 300, "强力压缩 DPI", out dpi, out error) || !TryInt(CompressionQuality, 20, 95, "JPEG 质量", out quality, out error))) return false;
        options = new PdfOptions(hasPdf ? EmptyToNull(PdfPageRange) : null, CompressPdf ? 6 : 0, hasPdf ? EmptyToNull(PdfWatermark) : null, hasPdf ? ParseRotation() : 0, additional, hasPdf && RasterCompressPdf, dpi, quality);
        return true;
    }
    private int ParseRotation() => int.TryParse(SelectedRotation.TrimEnd('°'), out var value) ? value : 0;
    private bool ConfirmRasterCompression(bool applies) => !applies || !RasterCompressPdf || ConfirmationWindow.Confirm(this, "强力压缩会把页面栅格化，原有可选文字、链接、批注和表单将无法保留。确定继续吗？", "确认强力压缩");
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool TryInt(string text, int min, int max, string name, out int value, out string error)
    {
        if (int.TryParse(text, out value) && value >= min && value <= max) { error = ""; return true; }
        error = $"{name} 必须是 {min}–{max} 之间的整数。"; return false;
    }
    private void QueueChanged(object? sender, QueueItem item)
    {
        if (item.Result is { } result)
        {
            lock (_historyGate)
                if (_recordedTasks.Add(item.Id))
                {
                    var entry = new HistoryEntry(DateTimeOffset.Now, item.Request.InputPath, item.Request.TargetFormat, result.Status, result.ErrorMessage, result.OutputFiles, result.Warnings, item.Request);
                    _historyWrites.RemoveAll(x => x.IsCompleted);
                    _unsavedHistory.Add(entry);
                    _historyWrites.Add(SaveHistoryAsync(entry));
                }
        }
        Dispatcher.InvokeAsync(() =>
        {
            QueueItems.FirstOrDefault(x => x.Item.Id == item.Id)?.Refresh();
            if (!_exitInProgress && item.Status is ConversionStatus.Failed or ConversionStatus.PartialSucceeded)
                ShowError($"{Path.GetFileName(item.Request.InputPath)}：{item.Result?.ErrorMessage} 详情可在“任务队列”中查看。");
        });
    }
    private async Task SaveHistoryAsync(HistoryEntry entry)
    {
        try
        {
            await _history.AppendAsync(entry).ConfigureAwait(false);
            lock (_historyGate) _unsavedHistory.Remove(entry);
            await Dispatcher.InvokeAsync(() => HistoryItems.Insert(0, new(entry)));
        }
        catch (Exception ex) { DiagnosticLog.Write("history.save", ex); await Dispatcher.InvokeAsync(() => ShowError("转换历史保存失败：" + ex.Message)); }
    }
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
        if (row.Entry.Request?.Options is OcrOptions ocr)
        {
            _quickAction = "ocr";
            SelectedTarget = "可搜索 PDF (OCR)";
            SelectedOcrLanguage = ocr.Languages switch { "chi_sim" => "简体中文", "eng" => "英文", _ => "中英混合" };
            OcrPageRange = ocr.PageRange ?? ""; OcrDpi = ocr.Dpi.ToString(); OcrGrayscale = ocr.Grayscale; OnChanged(nameof(OcrGrayscale));
            OutputDirectory = row.Entry.Request.OutputDirectory ?? "";
            StatusText = "OCR 历史任务已重新添加，语言、页码、DPI 和灰度参数已恢复。";
        }
        else { SelectedTarget = row.Entry.TargetFormat; StatusText = "历史任务已重新添加到输入列表。"; }
    }
    private void RerunHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryRow row || row.Entry.Request is not { } request) { ShowError("这条旧历史未保存任务参数，请重新添加后设置转换参数。"); return; }
        if (!File.Exists(request.InputPath)) { ShowError("原输入文件已不存在。"); return; }
        AddQueueRequest(request); StatusText = "已按历史中保存的参数重新运行任务。";
    }
    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmationWindow.Confirm(this, "确定清空全部转换历史吗？此操作不会删除输出文件。", "清空历史")) return;
        Task[] writes; lock (_historyGate) writes = _historyWrites.ToArray();
        await Task.WhenAll(writes);
        await _history.ClearAsync(); HistoryItems.Clear(); StatusText = "历史记录已清空。";
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_exitInProgress) return;
        if (_queue.HasActiveItems && !ConfirmationWindow.Confirm(this, "仍有任务正在运行。确定取消任务、等待清理完成后退出吗？", "任务仍在运行")) return;
        _exitInProgress = true; IsEnabled = false; StatusText = "正在取消任务并等待临时文件和转换进程清理…";
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        await _queue.StopAsync();
        Task[] writes; lock (_historyGate) writes = _historyWrites.ToArray();
        await Task.WhenAll(writes);
        HistoryEntry[] pending; lock (_historyGate) pending = _unsavedHistory.ToArray();
        foreach (var entry in pending) await SaveHistoryAsync(entry);
        lock (_historyGate)
            if (_unsavedHistory.Count > 0) { _exitInProgress = false; ShowError("历史尚未保存，退出已暂停。请检查磁盘和历史目录权限后再次关闭窗口。"); return; }
        _queue.Changed -= QueueChanged;
        _allowClose = true; Close();
    }
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class HistoryRow(HistoryEntry entry)
{
    public HistoryEntry Entry { get; } = entry;
    public string TimeText => Entry.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string FileName => Path.GetFileName(Entry.InputPath);
    public string Target => Entry.Request is { } request ? ConversionLabels.Target(request) : Entry.TargetFormat.ToUpperInvariant();
    public string StatusText => ConversionLabels.Status(Entry.Status, Entry.Warnings?.Count > 0);
    public string Detail => (Entry.OutputFiles?.Count > 0 ? string.Join("；", Entry.OutputFiles) + (Entry.Error is { Length: > 0 } ? "；" + Entry.Error : "") : Entry.Error ?? "")
        + (Entry.Warnings?.Count > 0 ? "；警告：" + string.Join("；", Entry.Warnings) : "");
}

public sealed class QueueRow(QueueItem item) : INotifyPropertyChanged
{
    public QueueItem Item { get; } = item;
    public string FileName => Path.GetFileName(Item.Request.InputPath);
    public string Target => ConversionLabels.Target(Item.Request);
    public string StatusText => ConversionLabels.Status(Item.Status, Item.Result?.Warnings.Count > 0);
    public string ProgressText => $"{Item.Progress:0}%";
    public string Message => Item.Message;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() { foreach (var name in new[] { nameof(StatusText), nameof(ProgressText), nameof(Message) }) PropertyChanged?.Invoke(this, new(name)); }
}

public static class ConversionLabels
{
    public static string Target(ConversionRequest request) => request.Options switch { OcrOptions => "可搜索 PDF (OCR)", PdfSplitOptions => "PDF 拆分", _ => request.TargetFormat.ToUpperInvariant() };
    public static string Status(ConversionStatus status, bool warnings) => status switch
    {
        ConversionStatus.Waiting => "等待", ConversionStatus.Checking => "检测", ConversionStatus.Processing => "处理中",
        ConversionStatus.Succeeded => warnings ? "成功（有警告）" : "成功", ConversionStatus.PartialSucceeded => "部分成功",
        ConversionStatus.Cancelled => "已取消", _ => "失败"
    };
}
