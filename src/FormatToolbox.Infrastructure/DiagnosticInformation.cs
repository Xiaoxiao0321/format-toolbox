using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using FormatToolbox.Core;
using Microsoft.Win32;

namespace FormatToolbox.Infrastructure;

public static class DiagnosticInformation
{
    public static string Capture()
    {
        var text = new StringBuilder();
        text.AppendLine($"工具箱版本：{Assembly.GetEntryAssembly()?.GetName().Version}");
        text.AppendLine($"系统：{RuntimeInformation.OSDescription}；进程架构：{RuntimeInformation.ProcessArchitecture}");
        text.AppendLine("注册表检测视图：Registry64、Registry32（优先当前进程视图）");
        foreach (var progId in new[] { "Word.Application", "Excel.Application", "PowerPoint.Application", "kwps.application", "ket.application", "kwpp.application", "AutoCAD.Application" })
        {
            try
            {
                var registration = OfficeComDetector.Find(progId);
                if (registration is null) { text.AppendLine($"组件 {progId}：未检测到可用注册"); continue; }
                using var classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, registration.View);
                using var server = classes.OpenSubKey(@"CLSID\" + registration.ClassId.ToString("B") + @"\LocalServer32");
                var command = Environment.ExpandEnvironmentVariables(server?.GetValue(null)?.ToString() ?? "").Trim();
                var end = command.StartsWith('"') ? command.IndexOf('"', 1) : command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) + 4;
                var path = command.StartsWith('"') && end > 1 ? command[1..end] : end >= 4 ? command[..end] : "";
                var version = File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).ProductVersion : null;
                text.AppendLine($"组件 {progId}：{registration.ProgId}；CLSID={registration.ClassId:B}；视图={registration.View}；{(progId.StartsWith("k") ? "WPS" : "软件")}版本={version ?? "未知（无法读取版本）"}");
            }
            catch (Exception ex) { text.AppendLine($"组件 {progId}：检测失败；HRESULT=0x{ex.HResult:X8}"); }
        }
        return text.ToString();
    }
}
