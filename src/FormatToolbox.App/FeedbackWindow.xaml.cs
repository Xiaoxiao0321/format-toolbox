using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using FormatToolbox.Core;
using FormatToolbox.Infrastructure;

namespace FormatToolbox.App;

public partial class FeedbackWindow : Window
{
    public const string FeedbackUrl = "https://format-toolbox-feedback.golden-elm-4905.chatgpt.site/";
    private readonly IConversionProvider[] _providers;

    public FeedbackWindow(IEnumerable<IConversionProvider> providers)
    {
        InitializeComponent();
        _providers = providers.ToArray();
        Loaded += LoadEnvironmentAsync;
    }

    private async void LoadEnvironmentAsync(object sender, RoutedEventArgs e)
    {
        var results = new Dictionary<string, AvailabilityResult>();
        var names = new Dictionary<string, string>
        {
            ["image.wic"] = "图片转换", ["pdf.pdfsharp"] = "PDF 工具",
            ["pdf.pdfium-render"] = "PDF 页面渲染", ["ocr.tesseract"] = "离线 OCR",
            ["office.word"] = "Word / WPS 文字", ["office.excel"] = "Excel / WPS 表格",
            ["office.powerpoint"] = "PowerPoint / WPS 演示", ["autocad.dwg"] = "AutoCAD"
        };
        foreach (var provider in _providers)
        {
            try { results[names.GetValueOrDefault(provider.Id, provider.Id)] = await provider.CheckAvailabilityAsync(); }
            catch (Exception ex)
            {
                DiagnosticLog.Write("feedback.environment", ex);
                results[names.GetValueOrDefault(provider.Id, provider.Id)] = new(false);
            }
        }
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "未知";
        EnvironmentText.Text = FeedbackEnvironmentFormatter.Format(version, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), Environment.Version.ToString(),
            RuntimeDependencyDetector.DetectVcppX64().IsAvailable, results);
        CopyEnvironmentButton.IsEnabled = true;
        StatusText.Text = "环境信息已在本机生成，尚未复制或发送。";
    }

    private void OpenWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(this,
            "将使用系统默认浏览器打开在线反馈网页，需要联网。应用不会自动上传文档、日志或环境信息。是否继续？",
            "打开在线反馈", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
        TryUserAction("feedback.open-website", () => Process.Start(new ProcessStartInfo(FeedbackUrl) { UseShellExecute = true }),
            "已请求浏览器打开反馈网页。若没有打开，可复制网址后自行访问。", "无法打开浏览器，请复制网址后自行访问。");
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e) => CopyText(FeedbackUrl, "反馈网址已复制。");
    private void CopyEnvironment_Click(object sender, RoutedEventArgs e) => CopyText(EnvironmentText.Text, "环境信息已复制，请自行选择是否粘贴到反馈中。");
    private void CopyText(string text, string message) => TryUserAction("feedback.clipboard",
        () => System.Windows.Clipboard.SetText(text), message, "剪贴板暂时不可用，请稍后重试或手动复制。");

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => TryUserAction("feedback.open-logs", () =>
    {
        Directory.CreateDirectory(DiagnosticLog.DirectoryPath);
        Process.Start(new ProcessStartInfo(DiagnosticLog.DirectoryPath) { UseShellExecute = true });
    }, "已打开本地日志目录；请检查敏感内容后再自行发送。", "无法打开日志目录，请稍后重试。");

    private void TryUserAction(string area, Action action, string success, string failure)
    {
        try { action(); StatusText.Text = success; }
        catch (Exception ex)
        {
            DiagnosticLog.Write(area, ex);
            StatusText.Text = failure;
            System.Windows.MessageBox.Show(this, failure, "意见反馈", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
