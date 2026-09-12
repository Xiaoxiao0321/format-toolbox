using Microsoft.Win32;

namespace FormatToolbox.Infrastructure;

public sealed record RuntimeDependencyStatus(bool IsAvailable, string DisplayText);

public static class RuntimeDependencyDetector
{
    public static RuntimeDependencyStatus DetectVcppX64()
    {
        const string path = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            var installed = Convert.ToInt32(key?.GetValue("Installed", 0)) == 1;
            var version = key?.GetValue("Version")?.ToString();
            return installed
                ? new(true, $"Microsoft Visual C++ 2015–2022 x64 Runtime {version}".Trim())
                : new(false, "缺少 Microsoft Visual C++ 2015–2022 x64 Runtime，OCR 可能无法启动");
        }
        catch (Exception ex)
        {
            return new(false, "无法检测 Visual C++ Runtime：" + ex.Message);
        }
    }
}
