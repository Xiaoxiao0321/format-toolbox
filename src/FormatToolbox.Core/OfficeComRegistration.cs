using Microsoft.Win32;

namespace FormatToolbox.Core;

public sealed record OfficeComRegistration(Guid ClassId, string ProgId, RegistryView View, string? ServerCommand = null)
{
    public bool IsWps => ServerCommand is { } command && (command.Contains("wps", StringComparison.OrdinalIgnoreCase) || command.Contains("kingsoft", StringComparison.OrdinalIgnoreCase) || command.Contains(@"\et.exe", StringComparison.OrdinalIgnoreCase));
}

public static class OfficeComDetector
{
    public static OfficeComRegistration? Find(string progId) => Find(progId, ReadValue);

    // UI detection and worker activation must resolve the same registered class.
    public static OfficeComRegistration? Find(string progId, Func<RegistryView, string, string?> readValue)
    {
        var names = progId.Equals("kwps.application", StringComparison.OrdinalIgnoreCase)
            ? new[] { progId, "kwps.application.1", "wps.application", "wps.application.1" }
            : new[] { progId, progId + ".1" };
        var nativeView = Environment.Is64BitProcess ? RegistryView.Registry64 : RegistryView.Registry32;
        foreach (var view in new[] { nativeView, nativeView == RegistryView.Registry64 ? RegistryView.Registry32 : RegistryView.Registry64 })
        {
            foreach (var name in names)
            {
                var current = readValue(view, name + @"\CurVer");
                foreach (var candidate in new[] { current, name }.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    if (!Guid.TryParse(readValue(view, candidate + @"\CLSID"), out var clsid)) continue;
                    var key = @"CLSID\" + clsid.ToString("B");
                    var localServer = readValue(view, key + @"\LocalServer32");
                    var inprocServer = view == nativeView ? readValue(view, key + @"\InprocServer32") : null;
                    // Executable COM servers support cross-bitness; in-process DLLs do not.
                    if (!string.IsNullOrWhiteSpace(localServer) || !string.IsNullOrWhiteSpace(inprocServer))
                        return new(clsid, candidate!, view, localServer ?? inprocServer);
                }
            }
        }
        return null;
    }

    private static string? ReadValue(RegistryView view, string path)
    {
        using var classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
        using var key = classes.OpenSubKey(path);
        return key?.GetValue(null)?.ToString();
    }
}
