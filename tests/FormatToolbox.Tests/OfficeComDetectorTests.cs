using FormatToolbox.Core;
using Microsoft.Win32;
using Xunit;

namespace FormatToolbox.Tests;

public sealed class OfficeComDetectorTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Kingsoft\\WPS Office\\office6\\et.exe\" /automation", true)]
    [InlineData("C:\\Apps\\office6\\et.exe /automation", true)]
    [InlineData("\"C:\\Program Files\\Microsoft Office\\root\\Office16\\EXCEL.EXE\" /automation", false)]
    public void Recognizes_real_WPS_server_for_Excel_compatibility_registration(string server, bool wps)
        => Assert.Equal(wps, new OfficeComRegistration(Guid.NewGuid(), "Excel.Application", RegistryView.Registry64, server).IsWps);
    [Theory]
    [InlineData(RegistryView.Registry32, "kwps.application")]
    [InlineData(RegistryView.Registry64, "kwps.application")]
    [InlineData(RegistryView.Registry32, "wps.application.1")]
    public void Finds_wps_executable_in_either_registry_view(RegistryView view, string progId)
    {
        var clsid = Guid.NewGuid();
        var entries = new Dictionary<(RegistryView, string), string>
        {
            [(view, progId + @"\CLSID")] = clsid.ToString("B"),
            [(view, @"CLSID\" + clsid.ToString("B") + @"\LocalServer32")] = "wps.exe /automation"
        };
        var result = OfficeComDetector.Find("kwps.application", (v, path) => entries.GetValueOrDefault((v, path)));
        Assert.NotNull(result);
        Assert.Equal(clsid, result.ClassId);
        Assert.Equal(view, result.View);
        Assert.Equal(progId, result.ProgId);
    }

    [Fact]
    public void Resolves_versioned_registration_and_rejects_missing_server()
    {
        var clsid = Guid.NewGuid();
        var entries = new Dictionary<string, string>
        {
            [@"kwps.application\CurVer"] = "kwps.application.42",
            [@"kwps.application.42\CLSID"] = clsid.ToString("B")
        };
        string? Read(RegistryView view, string path) => view == RegistryView.Registry32 ? entries.GetValueOrDefault(path) : null;
        Assert.Null(OfficeComDetector.Find("kwps.application", Read));
        entries[@"CLSID\" + clsid.ToString("B") + @"\LocalServer32"] = "wps.exe";
        Assert.Equal("kwps.application.42", OfficeComDetector.Find("kwps.application", Read)!.ProgId);
    }

    [Fact]
    public void Rejects_opposite_bitness_in_process_dll()
    {
        var otherView = Environment.Is64BitProcess ? RegistryView.Registry32 : RegistryView.Registry64;
        var clsid = Guid.NewGuid();
        var entries = new Dictionary<string, string>
        {
            [@"kwps.application\CLSID"] = clsid.ToString("B"),
            [@"CLSID\" + clsid.ToString("B") + @"\InprocServer32"] = "wps.dll"
        };
        Assert.Null(OfficeComDetector.Find("kwps.application", (v, path) => v == otherView ? entries.GetValueOrDefault(path) : null));
    }
}
