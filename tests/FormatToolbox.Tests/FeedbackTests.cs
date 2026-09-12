using FormatToolbox.Core;
using FormatToolbox.Infrastructure;
using Xunit;

namespace FormatToolbox.Tests;

public sealed class FeedbackTests
{
    [Fact]
    public void Environment_information_contains_only_selected_diagnostic_fields()
    {
        var text = FeedbackEnvironmentFormatter.Format("1.0.0", "Windows 11", "X64", "8.0.0", true,
            new Dictionary<string, AvailabilityResult> { ["PDF"] = new(true, "6.2.4") });
        Assert.Contains("格式转换工具箱 1.0.0", text);
        Assert.Contains("Windows 11", text);
        Assert.Contains("PDF：可用 (6.2.4)", text);
        Assert.Contains("VC++ x64 运行库：可用", text);
    }

    [Fact]
    public void Environment_information_does_not_include_dependency_exception_details()
    {
        const string privatePath = @"C:\Users\PrivateName\Documents\机密合同.docx";
        var text = FeedbackEnvironmentFormatter.Format("1.0.0", "Windows 10", "X64", "8.0.0", false,
            new Dictionary<string, AvailabilityResult> { ["Office"] = new(false, Reason: privatePath) });
        Assert.Contains("Office：不可用", text);
        Assert.Contains("VC++ x64 运行库：未检测到", text);
        Assert.DoesNotContain(privatePath, text);
        Assert.DoesNotContain("PrivateName", text);
    }
}
