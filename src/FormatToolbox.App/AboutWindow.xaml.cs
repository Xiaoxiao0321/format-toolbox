using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using FormatToolbox.Infrastructure;

namespace FormatToolbox.App;

public partial class AboutWindow : Window
{
    private readonly string _noticesPath = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText.Text = $"版本 {version} · Windows 10/11 x64";
        var runtime = RuntimeDependencyDetector.DetectVcppX64();
        RuntimeText.Text = (runtime.IsAvailable ? "✓ " : "⚠ ") + runtime.DisplayText;
        NoticesText.Text = File.Exists(_noticesPath) ? File.ReadAllText(_noticesPath) : "未找到第三方软件声明文件。";
    }
    private void OpenNotices_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_noticesPath)) Process.Start(new ProcessStartInfo(_noticesPath) { UseShellExecute = true });
    }
    private void OpenLicenses_Click(object sender, RoutedEventArgs e)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "licenses");
        if (Directory.Exists(directory)) Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
