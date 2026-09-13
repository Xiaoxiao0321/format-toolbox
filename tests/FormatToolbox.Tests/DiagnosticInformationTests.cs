using FormatToolbox.Infrastructure;
using Xunit;

namespace FormatToolbox.Tests;

public sealed class DiagnosticInformationTests
{
    [Fact]
    public void Snapshot_includes_versions_components_and_registry_views_without_local_paths()
    {
        var text = DiagnosticInformation.Capture();
        Assert.Contains("工具箱版本：", text);
        Assert.Contains("Registry64", text);
        Assert.Contains("Registry32", text);
        foreach (var component in new[] { "kwps.application", "ket.application", "kwpp.application", "Word.Application", "AutoCAD.Application" })
            Assert.Contains("组件 " + component, text);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile)) Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@":\", text);
        Assert.DoesNotContain(@"\LocalServer32", text);
    }

    [Fact]
    public void Copy_information_has_recent_error_section()
    {
        Assert.Contains("近期错误码（本次运行）：", DiagnosticLog.GetInformation());
    }
}
