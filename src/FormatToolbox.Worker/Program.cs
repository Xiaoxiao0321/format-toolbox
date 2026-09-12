using System.Runtime.InteropServices;
using System.Text.Json;
using FormatToolbox.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var values = args.Chunk(2).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]);
        if (!values.TryGetValue("--engine", out var engine) || !values.TryGetValue("--input", out var input) || !values.TryGetValue("--output", out var output))
            return Write(false, ErrorCodes.EngineFailure, "缺少 Worker 参数。", "worker", []);
        try
        {
            return engine switch
            {
                "msoffice.word" => ConvertWord(input, output, "Word.Application", "Microsoft Word"),
                "msoffice.excel" => ConvertExcel(input, output, "Excel.Application", "Microsoft Excel", false),
                "msoffice.powerpoint" => ConvertPowerPoint(input, output, "PowerPoint.Application", "Microsoft PowerPoint"),
                "wps.writer" => ConvertWord(input, output, "kwps.application", "WPS 文字"),
                "wps.spreadsheet" => ConvertExcel(input, output, "ket.application", "WPS 表格", true),
                "wps.presentation" => ConvertPowerPoint(input, output, "kwpp.application", "WPS 演示"),
                "autocad.dwg" => ConvertAutoCad(input, output),
                _ => Write(false, ErrorCodes.UnsupportedFormat, "未知转换引擎。", engine, [])
            };
        }
        catch (COMException ex) { return Write(false, ErrorCodes.InvalidOrEncrypted, Sanitize(ex.Message), engine, []); }
        catch (UnauthorizedAccessException ex) { return Write(false, ErrorCodes.AccessDenied, Sanitize(ex.Message), engine, []); }
        catch (Exception ex) { return Write(false, ErrorCodes.EngineFailure, Sanitize(ex.Message), engine, []); }
    }

    private static int ConvertWord(string input, string output, string progId, string displayName)
    {
        dynamic? app = null, doc = null;
        try
        {
            app = Create(progId); app.Visible = false; app.DisplayAlerts = 0;
            try { app.AutomationSecurity = 3; } catch { }
            doc = app.Documents.Open(input, ReadOnly: true, AddToRecentFiles: false, Visible: false);
            doc.ExportAsFixedFormat(output, 17, false, 0, 0, 1, 9999, 0, true, true, 0, true, true, false);
            return Write(true, null, null, $"{displayName} {app.Version}", []);
        }
        finally { try { doc?.Close(false); } catch { } try { app?.Quit(false); } catch { } Release(doc); Release(app); }
    }

    private static int ConvertExcel(string input, string output, string progId, string displayName, bool useWpsPageSetup)
    {
        dynamic? app = null, book = null;
        try
        {
            if (useWpsPageSetup) WaitForRecentWriteToSettle(input, TimeSpan.FromSeconds(10));
            app = Create(progId); app.Visible = false; app.DisplayAlerts = false;
            try { app.AutomationSecurity = 3; } catch { }
            book = app.Workbooks.Open(input, UpdateLinks: 0, ReadOnly: true, IgnoreReadOnlyRecommended: true, AddToMru: false);
            foreach (dynamic sheet in book.Worksheets)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace((string)sheet.PageSetup.PrintArea))
                    {
                        sheet.PageSetup.Zoom = false;
                        sheet.PageSetup.FitToPagesWide = 1;
                        // WPS treats VT_BOOL false as an invalid/near-zero page scale here.
                        // A single-page height preserves readable output; Excel supports false as automatic height.
                        if (useWpsPageSetup) sheet.PageSetup.FitToPagesTall = 1;
                        else sheet.PageSetup.FitToPagesTall = false;
                    }
                }
                finally { Release(sheet); }
            }
            book.ExportAsFixedFormat(0, output, 0, true, false);
            return Write(true, null, null, $"{displayName} {app.Version}", []);
        }
        finally { try { book?.Close(false); } catch { } try { app?.Quit(); } catch { } Release(book); Release(app); }
    }

    private static void WaitForRecentWriteToSettle(string path, TimeSpan minimumAge)
    {
        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
        var delay = minimumAge - age;
        if (delay > TimeSpan.Zero) Thread.Sleep(delay);
    }

    private static int ConvertPowerPoint(string input, string output, string progId, string displayName)
    {
        dynamic? app = null, presentation = null;
        try
        {
            app = Create(progId);
            presentation = app.Presentations.Open(input, ReadOnly: -1, Untitled: 0, WithWindow: 0);
            presentation.SaveAs(output, 32);
            return Write(true, null, null, $"{displayName} {app.Version}", []);
        }
        finally { try { presentation?.Close(); } catch { } try { app?.Quit(); } catch { } Release(presentation); Release(app); }
    }

    private static int ConvertAutoCad(string input, string output)
    {
        dynamic? app = null, document = null;
        var warnings = new List<string>();
        var temporaryPdfs = new List<string>();
        object? originalBackgroundPlot = null;
        try
        {
            app = Create("AutoCAD.Application"); app.Visible = false;
            document = app.Documents.Open(input, true);
            try { originalBackgroundPlot = document.GetVariable("BACKGROUNDPLOT"); document.SetVariable("BACKGROUNDPLOT", 0); } catch { warnings.Add("无法禁用 AutoCAD 后台打印，布局输出可能需要更长时间。"); }
            var layouts = new List<(string Name, int TabOrder)>();
            foreach (dynamic layout in document.Layouts)
            {
                try
                {
                    if ((bool)layout.ModelType) { warnings.Add("已跳过 Model 布局。"); continue; }
                    layouts.Add(((string)layout.Name, (int)layout.TabOrder));
                }
                catch (Exception ex) { warnings.Add($"布局信息读取失败：{Sanitize(ex.Message)}"); }
                finally { Release(layout); }
            }
            foreach (var item in layouts.OrderBy(x => x.TabOrder))
            {
                dynamic? layout = null;
                try
                {
                    layout = document.Layouts.Item(item.Name);
                    document.ActiveLayout = layout;
                    try { layout.RefreshPlotDeviceInfo(); } catch { }
                    var config = (string?)layout.ConfigName;
                    if (string.IsNullOrWhiteSpace(config) || config.Equals("None", StringComparison.OrdinalIgnoreCase))
                    {
                        config = "DWG To PDF.pc3"; layout.ConfigName = config;
                        warnings.Add($"布局 {item.Name} 未配置打印设备，已使用 DWG To PDF.pc3。");
                    }
                    var temp = Path.Combine(Path.GetDirectoryName(output)!, $".{Guid.NewGuid():N}-{item.TabOrder:D4}.pdf");
                    if (!(bool)document.Plot.PlotToFile(temp, config)) { warnings.Add($"布局 {item.Name} 打印失败，已跳过。"); continue; }
                    temporaryPdfs.Add(temp);
                }
                catch (Exception ex) { warnings.Add($"布局 {item.Name} 无法打印，已跳过：{Sanitize(ex.Message)}"); }
                finally { Release(layout); }
            }
            if (temporaryPdfs.Count == 0) throw new InvalidOperationException("图纸中没有成功输出的可打印布局。");
            MergePdfFiles(temporaryPdfs, output);
            return Write(true, null, null, $"AutoCAD {app.Version}", warnings);
        }
        finally
        {
            if (document is not null && originalBackgroundPlot is not null) try { document.SetVariable("BACKGROUNDPLOT", originalBackgroundPlot); } catch { }
            foreach (var file in temporaryPdfs) if (File.Exists(file)) File.Delete(file);
            try { document?.Close(false); } catch { } try { app?.Quit(); } catch { } Release(document); Release(app);
        }
    }

    private static void MergePdfFiles(IReadOnlyList<string> inputs, string output)
    {
        using var merged = new PdfDocument();
        foreach (var path in inputs)
        {
            using var source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            foreach (var page in source.Pages) merged.AddPage(page);
        }
        if (merged.PageCount == 0) throw new InvalidOperationException("AutoCAD 输出的布局 PDF 不包含页面。");
        merged.Save(output);
    }

    private static dynamic Create(string progId) => Activator.CreateInstance(Type.GetTypeFromProgID(progId, true)!)!;
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    private static string Sanitize(string message) => message.Replace(Environment.UserName, "<user>", StringComparison.OrdinalIgnoreCase);
    private static int Write(bool success, string? code, string? message, string engine, List<string> warnings)
    {
        Console.Write(JsonSerializer.Serialize(new { Success = success, ErrorCode = code, Message = message, Engine = engine, Warnings = warnings }));
        return success ? 0 : 1;
    }
}
