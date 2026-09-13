using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;
using FormatToolbox.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

internal static class Program
{
    private static string? _cancellationPath, _ownedProcessPath;
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [STAThread]
    private static int Main(string[] args)
    {
        var values = args.Chunk(2).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]);
        if (!values.TryGetValue("--engine", out var engine) || !values.TryGetValue("--input", out var input) || !values.TryGetValue("--output", out var output))
            return Write(false, ErrorCodes.EngineFailure, "缺少 Worker 参数。", "worker", []);
        values.TryGetValue("--cancel", out _cancellationPath);
        values.TryGetValue("--owned-process", out _ownedProcessPath);
        try
        {
            CheckCancellation();
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
        catch (OperationCanceledException) { return Write(false, ErrorCodes.Cancelled, "任务已取消。", engine, []); }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80040154)) { return Write(false, ErrorCodes.DependencyMissing, Sanitize(ex.Message) + " 自动化组件未注册，请使用 Office/WPS 安装程序修复后重试。", engine, [], ex.HResult); }
        catch (COMException ex) { return Write(false, ErrorCodes.InvalidOrEncrypted, Sanitize(ex.Message), engine, [], ex.HResult); }
        catch (UnauthorizedAccessException ex) { return Write(false, ErrorCodes.AccessDenied, Sanitize(ex.Message), engine, [], ex.HResult); }
        catch (Exception ex) { return Write(false, ErrorCodes.EngineFailure, Sanitize(ex.Message), engine, [], ex.HResult); }
    }

    private static int ConvertWord(string input, string output, string progId, string displayName)
    {
        dynamic? app = null, doc = null;
        try
        {
            app = Create(progId); app.Visible = false; app.DisplayAlerts = 0;
            try { app.AutomationSecurity = 3; } catch { }
            doc = app.Documents.Open(input, ReadOnly: true, AddToRecentFiles: false, Visible: false);
            CheckCancellation();
            doc.ExportAsFixedFormat(output, 17, false, 0, 0, 1, 9999, 0, true, true, 0, true, true, false);
            CheckCancellation();
            return Write(true, null, null, $"{displayName} {app.Version}", []);
        }
        finally { try { doc?.Close(false); } catch { } try { app?.Quit(false); } catch { } Release(doc); Release(app); }
    }

    private static int ConvertExcel(string input, string output, string progId, string displayName, bool useWpsPageSetup)
    {
        dynamic? app = null, book = null;
        var compatibilityWps = !useWpsPageSetup && OfficeComDetector.Find(progId)?.IsWps == true;
        if (compatibilityWps)
        {
            useWpsPageSetup = true; displayName = "WPS 表格";
            // The Excel compatibility class can truncate cells during PDF export.
            // Activate the native WPS class when registered, even if the Office route was selected.
            if (OfficeComDetector.Find("ket.application") is not null) progId = "ket.application";
        }
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
                    CheckCancellation();
                    if (string.IsNullOrWhiteSpace((string)sheet.PageSetup.PrintArea))
                    {
                        sheet.PageSetup.Zoom = false;
                        sheet.PageSetup.FitToPagesWide = 1;
                        // WPS requires numeric zero for unrestricted vertical pagination.
                        // VT_BOOL false can produce invalid scaling; height 1 shrinks long tables.
                        if (useWpsPageSetup) sheet.PageSetup.FitToPagesTall = 0;
                        else sheet.PageSetup.FitToPagesTall = false;
                    }
                }
                finally { Release(sheet); }
            }
            book.ExportAsFixedFormat(0, output, 0, true, false);
            CheckCancellation();
            return Write(true, null, null, $"{displayName} {app.Version}", compatibilityWps ? ["Excel 兼容组件由 WPS 提供，已优先使用 WPS 原生组件及分页设置。"] : []);
        }
        finally { try { book?.Close(false); } catch { } try { app?.Quit(); } catch { } Release(book); Release(app); }
    }

    private static void WaitForRecentWriteToSettle(string path, TimeSpan minimumAge)
    {
        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
        var delay = minimumAge - age;
        while (delay > TimeSpan.Zero)
        {
            CheckCancellation();
            Thread.Sleep(delay > TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : delay);
            delay = minimumAge - (DateTime.UtcNow - File.GetLastWriteTimeUtc(path));
        }
    }

    private static int ConvertPowerPoint(string input, string output, string progId, string displayName)
    {
        dynamic? app = null, presentation = null;
        try
        {
            app = Create(progId);
            presentation = app.Presentations.Open(input, ReadOnly: -1, Untitled: 0, WithWindow: 0);
            CheckCancellation();
            presentation.SaveAs(output, 32);
            CheckCancellation();
            return Write(true, null, null, $"{displayName} {app.Version}", []);
        }
        finally { try { presentation?.Close(); } catch { } try { app?.Quit(); } catch { } Release(presentation); Release(app); }
    }

    private static int ConvertAutoCad(string input, string output)
    {
        dynamic? app = null, document = null;
        var warnings = new List<string>();
        var temporaryPdfs = new List<string>();
        var printedPdfs = new List<string>();
        var partialSuccess = false;
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
                    CheckCancellation();
                    if ((bool)layout.ModelType) { warnings.Add("已跳过 Model 布局。"); continue; }
                    layouts.Add(((string)layout.Name, (int)layout.TabOrder));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { partialSuccess = true; warnings.Add($"布局信息读取失败：{Sanitize(ex.Message)}"); }
                finally { Release(layout); }
            }
            foreach (var item in layouts.OrderBy(x => x.TabOrder))
            {
                dynamic? layout = null;
                try
                {
                    CheckCancellation();
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
                    temporaryPdfs.Add(temp);
                    if (!(bool)document.Plot.PlotToFile(temp, config)) { partialSuccess = true; warnings.Add($"布局 {item.Name} 打印失败，已跳过。"); continue; }
                    printedPdfs.Add(temp);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { partialSuccess = true; warnings.Add($"布局 {item.Name} 无法打印，已跳过：{Sanitize(ex.Message)}"); }
                finally { Release(layout); }
            }
            if (printedPdfs.Count == 0) return Write(false, ErrorCodes.EngineFailure, "图纸中没有成功输出的可打印布局。", $"AutoCAD {app.Version}", warnings);
            MergePdfFiles(printedPdfs, output);
            CheckCancellation();
            return Write(true, null, partialSuccess ? $"仅输出 {printedPdfs.Count}/{layouts.Count} 个已读取布局；部分布局读取或打印失败，请检查警告。" : null, $"AutoCAD {app.Version}", warnings, partialSuccess: partialSuccess);
        }
        finally
        {
            if (document is not null && originalBackgroundPlot is not null) try { document.SetVariable("BACKGROUNDPLOT", originalBackgroundPlot); } catch { }
            foreach (var file in temporaryPdfs) try { if (File.Exists(file)) File.Delete(file); } catch { }
            try { document?.Close(false); } catch { } try { app?.Quit(); } catch { } Release(document); Release(app);
        }
    }

    private static void MergePdfFiles(IReadOnlyList<string> inputs, string output)
    {
        using var merged = new PdfDocument();
        foreach (var path in inputs)
        {
            CheckCancellation();
            using var source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            foreach (var page in source.Pages) merged.AddPage(page);
        }
        if (merged.PageCount == 0) throw new InvalidOperationException("AutoCAD 输出的布局 PDF 不包含页面。");
        merged.Save(output);
    }

    private static dynamic Create(string progId)
    {
        CheckCancellation();
        var registration = OfficeComDetector.Find(progId)
            ?? throw new COMException("无法找到自动化组件，请先打开对应软件完成首次启动，或使用其安装程序修复组件注册。", unchecked((int)0x80040154));
        var processes = Process.GetProcesses();
        var existing = processes.Select(x => x.Id).ToHashSet();
        foreach (var process in processes) process.Dispose();
        dynamic app = Activator.CreateInstance(Type.GetTypeFromCLSID(registration.ClassId, true)!)!;
        try
        {
            if (_ownedProcessPath is not null)
            {
                nint handle = (nint)Convert.ToInt64(app.HWND);
                GetWindowThreadProcessId(handle, out var pid);
                if (pid != 0 && !existing.Contains((int)pid))
                {
                    using var process = Process.GetProcessById((int)pid);
                    File.WriteAllText(_ownedProcessPath, JsonSerializer.Serialize(new OwnedAutomationProcess((int)pid, process.StartTime.ToUniversalTime().Ticks)));
                }
            }
        }
        catch { } // Some automation engines do not expose a window handle.
        return app;
    }
    private static void CheckCancellation() { if (_cancellationPath is not null && File.Exists(_cancellationPath)) throw new OperationCanceledException(); }
    private static void Release(object? value) { try { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); } catch { } }
    private static string Sanitize(string message) => message.Replace(Environment.UserName, "<user>", StringComparison.OrdinalIgnoreCase);
    private static int Write(bool success, string? code, string? message, string engine, List<string> warnings, int? hresult = null, bool partialSuccess = false)
    {
        Console.Write(JsonSerializer.Serialize(new WorkerResponse(success, code, message, engine, warnings, hresult, partialSuccess)));
        return success ? 0 : 1;
    }
}
