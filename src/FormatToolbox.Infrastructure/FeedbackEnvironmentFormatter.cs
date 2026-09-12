using System.Text;
using FormatToolbox.Core;

namespace FormatToolbox.Infrastructure;

public static class FeedbackEnvironmentFormatter
{
    public static string Format(string appVersion, string operatingSystem, string architecture,
        string runtimeVersion, bool vcppAvailable, IReadOnlyDictionary<string, AvailabilityResult> engines)
    {
        var text = new StringBuilder();
        text.AppendLine($"软件：格式转换工具箱 {appVersion}");
        text.AppendLine($"系统：{operatingSystem}");
        text.AppendLine($"程序架构：{architecture}");
        text.AppendLine($".NET：{runtimeVersion}");
        text.AppendLine($"VC++ x64 运行库：{(vcppAvailable ? "可用" : "未检测到")}");
        text.AppendLine("转换引擎：");
        foreach (var (name, status) in engines)
            text.AppendLine($"- {name}：{(status.IsAvailable ? "可用" : "不可用")}{(status.IsAvailable && !string.IsNullOrWhiteSpace(status.Version) ? $" ({status.Version})" : "")}");
        // Deliberately exclude availability reasons: exceptions may contain local paths or user names.
        text.AppendLine("不含用户文件、文件路径、历史记录、设备名称或诊断日志；请自行选择是否发送以上信息。");
        return text.ToString();
    }
}
