using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FormatToolbox.App;
using FormatToolbox.Core;
using FormatToolbox.Infrastructure;
using FormatToolbox.Infrastructure.Providers;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;
using ValidationResult = FormatToolbox.Core.ValidationResult;

namespace FormatToolbox.Tests;

public sealed class TaskFlowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FormatToolbox.TaskFlow", Guid.NewGuid().ToString("N"));
    public TaskFlowTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Split_uses_dedicated_queue_route_and_can_retry_with_original_parameters()
    {
        var input = CreatePdf();
        var registry = new ConversionRegistry([new PdfConversionProvider(), new PdfSplitProvider()]);
        Assert.IsType<PdfConversionProvider>(registry.Resolve(new ConversionRequest(input, "pdf")));
        var request = new ConversionRequest(input, "pdf", _directory, Options: new PdfSplitOptions(PageRanges: "1-2;3"), OutputFileName: "sections");
        Assert.IsType<PdfSplitProvider>(registry.Resolve(request));
        var queue = new ConversionQueue(registry);
        var first = queue.Enqueue(request);
        await queue.WaitForIdleAsync();
        Assert.Equal(ConversionStatus.Succeeded, first.Status);
        Assert.Equal(2, first.Result!.OutputFiles.Count);
        Assert.Contains("拆分", new QueueRow(first).Target);
        using (var part = PdfReader.Open(first.Result.OutputFiles[0], PdfDocumentOpenMode.Import)) Assert.Equal(2, part.PageCount);
        var retry = queue.Enqueue(first.Request);
        await queue.WaitForIdleAsync();
        Assert.Equal(ConversionStatus.Succeeded, retry.Status);
        Assert.Empty(first.Result.OutputFiles.Intersect(retry.Result!.OutputFiles));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelling_multi_output_task_preserves_completed_results_and_reports_partial_success(bool render)
    {
        var input = CreatePdf();
        using var cancellation = new CancellationTokenSource();
        IConversionProvider provider = render ? new PdfRenderProvider() : new PdfSplitProvider();
        ConversionOptions options = render ? new PdfRenderOptions(Dpi: 72) : new PdfSplitOptions();
        var result = await provider.ConvertAsync(new(input, render ? "png" : "pdf", _directory, Options: options), new CallbackProgress(_ => cancellation.Cancel()), cancellation.Token);
        Assert.Equal(ConversionStatus.PartialSucceeded, result.Status);
        Assert.Equal(ErrorCodes.Cancelled, result.ErrorCode);
        Assert.Single(result.OutputFiles); Assert.True(File.Exists(result.OutputFiles[0]));
        Assert.Contains("1/3", result.ErrorMessage);
        Assert.Equal("部分成功", new HistoryRow(new(DateTimeOffset.Now, input, "pdf", result.Status, result.ErrorMessage, result.OutputFiles)).StatusText);
        Assert.Contains("取消", new HistoryRow(new(DateTimeOffset.Now, input, "pdf", result.Status, result.ErrorMessage, result.OutputFiles)).Detail);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        using var unlocked = File.Open(input, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData("1-2-3")]
    [InlineData("1,")]
    [InlineData(",")]
    public async Task Invalid_ranges_do_not_report_empty_success(string range)
    {
        var result = await new PdfRenderProvider().ConvertAsync(new(CreatePdf(), "png", _directory, Options: new PdfRenderOptions(range, 72)), null, CancellationToken.None);
        Assert.Equal(ConversionStatus.Failed, result.Status); Assert.Empty(result.OutputFiles);
    }

    [Fact]
    public async Task Stop_cancels_running_and_waiting_tasks_and_waits_for_cleanup()
    {
        var provider = new CleanupProbe();
        var queue = new ConversionQueue(new ConversionRegistry([provider]), 1);
        var running = queue.Enqueue(new("running.pdf", "pdf"));
        var waiting = queue.Enqueue(new("waiting.pdf", "pdf"));
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = queue.StopAsync();
        await provider.CleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stop.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => queue.Enqueue(new("late.pdf", "pdf")));
        provider.AllowCleanup.TrySetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ConversionStatus.Cancelled, running.Status); Assert.Equal(ConversionStatus.Cancelled, waiting.Status);
        Assert.Equal(1, provider.Calls); Assert.False(queue.HasActiveItems); Assert.Equal(0, queue.ActiveCount);
    }

    [Fact]
    public async Task History_roundtrip_preserves_typed_options_and_legacy_entries_remain_readable()
    {
        var store = new HistoryStore(Path.Combine(_directory, "history.json"));
        ConversionOptions[] options = [new OcrOptions("eng", "2-3", 432, true), new PdfSplitOptions(2, "1;2-3"), new PdfRenderOptions("2", 144, 33), new PdfOptions(AdditionalInputs: ["other.pdf"], PageOrder: [3, 1]), new ImageOptions(45), new OfficeOptions(), new DwgOptions()];
        foreach (var option in options)
        {
            var request = new ConversionRequest("input.pdf", "pdf", "out", Options: option, OutputFileName: "saved");
            await store.AppendAsync(new(DateTimeOffset.Now, request.InputPath, request.TargetFormat, ConversionStatus.Succeeded, null, Request: request));
        }
        var entries = await store.LoadAsync();
        for (var i = 0; i < options.Length; i++)
        {
            Assert.Equal(options[i].GetType(), entries[i].Request!.Options!.GetType());
            Assert.Equal(JsonSerializer.Serialize<ConversionOptions>(options[i]), JsonSerializer.Serialize(entries[i].Request!.Options));
            Assert.Equal("saved", entries[i].Request!.OutputFileName);
        }
        Assert.Contains("OCR", new HistoryRow(entries[0]).Target);
        await File.WriteAllTextAsync(Path.Combine(_directory, "history.json"), "[{\"Timestamp\":\"2026-09-13T00:00:00Z\",\"InputPath\":\"old.pdf\",\"TargetFormat\":\"pdf\",\"Status\":3}]");
        Assert.Null((await store.LoadAsync()).Single().Request);
    }

    [Fact]
    public void Pdf_tools_enqueues_split_without_creating_outputs_in_dialog()
    {
        var input = CreatePdf();
        RunUi(async () =>
        {
            var requests = new List<ConversionRequest>();
            var window = new PdfToolsWindow(requests.Add, _directory) { InputPath = input, SelectedSplitMode = "指定范围", SplitParameter = "1-2;3", OutputBaseName = "parts" };
            Invoke(window, "Split_Click");
            var request = Assert.Single(requests);
            Assert.Equal("1-2;3", Assert.IsType<PdfSplitOptions>(request.Options).PageRanges);
            Assert.Empty(Directory.GetFiles(_directory, "parts*"));
            await CloseAsync(window);
        });
    }

    [Fact]
    public void Ocr_readd_restores_settings_and_exit_flushes_task_history()
    {
        var input = CreatePdf(); var historyPath = Path.Combine(_directory, "history.json");
        RunUi(async () =>
        {
            var store = new HistoryStore(historyPath);
            var window = new MainWindow(store);
            var options = new OcrOptions("eng", "2-3", 432, true);
            var request = new ConversionRequest(input, "pdf", _directory, Options: options);
            var entry = new HistoryEntry(DateTimeOffset.Now, input, "pdf", ConversionStatus.Succeeded, null, Request: request);
            window.HistoryItems.Add(new(entry));
            var historyList = (ListView)window.FindName("HistoryList"); historyList.ItemsSource = window.HistoryItems; historyList.SelectedItem = window.HistoryItems[0];
            Invoke(window, "ReAddHistory_Click");
            Assert.Contains("OCR", window.SelectedTarget); Assert.Equal("英文", window.SelectedOcrLanguage);
            Assert.Equal("2-3", window.OcrPageRange); Assert.Equal("432", window.OcrDpi); Assert.True(window.OcrGrayscale);
            // Missing input finishes immediately, exercising history writes still pending when Close is called.
            var failedRequest = request with { InputPath = Path.Combine(_directory, "missing.pdf") };
            Invoke(window, "AddQueueRequest", failedRequest);
            var queue = (ConversionQueue)typeof(MainWindow).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            await queue.WaitForIdleAsync();
            await CloseAsync(window);
            var saved = Assert.Single(await store.LoadAsync());
            Assert.Equal(options, Assert.IsType<OcrOptions>(saved.Request!.Options));
            Assert.Equal(ConversionStatus.Failed, saved.Status);
        });
    }

    [Fact]
    public void Dwg_layout_failure_is_partial_but_model_skip_is_a_success_with_warning()
    {
        var partial = new WorkerResponse(true, null, "已输出 1/2 个布局。", "AutoCAD", ["布局 B 打印失败。"], PartialSuccess: true);
        var restored = JsonSerializer.Deserialize<WorkerResponse>(JsonSerializer.Serialize(partial))!;
        var result = restored.ToResult("drawing.pdf", TimeSpan.Zero, "autocad.dwg");
        Assert.Equal(ConversionStatus.PartialSucceeded, result.Status); Assert.Single(result.OutputFiles);
        Assert.Equal(ErrorCodes.PartialOutput, result.ErrorCode); Assert.Contains("1/2", result.ErrorMessage);
        var benign = new WorkerResponse(true, null, null, "AutoCAD", ["已跳过 Model 布局。"]).ToResult("drawing.pdf", TimeSpan.Zero, "autocad.dwg");
        Assert.Equal(ConversionStatus.Succeeded, benign.Status); Assert.Single(benign.Warnings);
    }

    [Fact]
    public async Task Cancellation_aborts_process_start_while_native_ocr_output_is_open()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var nativeOutput = Task.Run(() => { using var scope = NativeOutputSynchronization.EnterOcrOutput(CancellationToken.None); entered.TrySetResult(); release.Wait(); });
        await entered.Task;
        try
        {
            var script = Path.Combine(_directory, "not-started.ps1"); await File.WriteAllTextAsync(script, "Set-Content -LiteralPath (Join-Path $PSScriptRoot 'pid') -Value $PID\n");
            using var cancellation = new CancellationTokenSource();
            var run = WorkerProcessRunner.RunAsync(StartInfo(script), _directory, TimeSpan.FromSeconds(20), cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(File.Exists(Path.Combine(_directory, "pid")));
        }
        finally { release.Set(); await nativeOutput; }
    }

    [Fact]
    public async Task Temporary_cleanup_waits_for_a_transient_file_lock()
    {
        var path = Path.Combine(_directory, "locked.tmp");
        var locked = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var cleanup = TemporaryFileCleanup.DeleteFileAsync(path, "test");
        Assert.False(cleanup.IsCompleted); locked.Dispose();
        await cleanup; Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Worker_cancellation_waits_for_cooperative_cleanup()
    {
        var script = Path.Combine(_directory, "probe.ps1");
        await File.WriteAllTextAsync(script, "Set-Content -LiteralPath (Join-Path $PSScriptRoot 'pid') -Value $PID\nwhile (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'cancel'))) { Start-Sleep -Milliseconds 30 }\nSet-Content -LiteralPath (Join-Path $PSScriptRoot 'cleaned') -Value 'done'\n");
        using var cancellation = new CancellationTokenSource();
        var run = WorkerProcessRunner.RunAsync(StartInfo(script), _directory, TimeSpan.FromSeconds(20), cancellation.Token);
        await WaitForFileAsync(Path.Combine(_directory, "pid"));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(File.Exists(Path.Combine(_directory, "cleaned")));
        AssertProcessExited(int.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, "pid"))));
    }

    [Fact]
    public async Task Worker_timeout_terminates_unresponsive_process()
    {
        var script = Path.Combine(_directory, "stuck.ps1");
        await File.WriteAllTextAsync(script, "Set-Content -LiteralPath (Join-Path $PSScriptRoot 'pid') -Value $PID\nStart-Sleep -Seconds 60\n");
        var run = WorkerProcessRunner.RunAsync(StartInfo(script), _directory, TimeSpan.FromSeconds(2), CancellationToken.None);
        await WaitForFileAsync(Path.Combine(_directory, "pid"));
        var pid = int.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, "pid")));
        await Assert.ThrowsAsync<TimeoutException>(() => run);
        AssertProcessExited(pid);
    }

    [Fact]
    public async Task Automation_cleanup_requires_matching_process_start_time()
    {
        var script = Path.Combine(_directory, "owned.ps1"); await File.WriteAllTextAsync(script, "Start-Sleep -Seconds 60\n");
        using var process = await Task.Run(() => NativeOutputSynchronization.StartProcess(StartInfo(script), CancellationToken.None));
        try
        {
            var manifest = Path.Combine(_directory, "automation.json");
            var owned = new OwnedAutomationProcess(process.Id, process.StartTime.ToUniversalTime().Ticks);
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(owned with { StartTimeUtcTicks = owned.StartTimeUtcTicks - 1 }));
            await WorkerProcessRunner.CleanupAutomationAsync(manifest); Assert.False(process.HasExited);
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(owned));
            await WorkerProcessRunner.CleanupAutomationAsync(manifest); Assert.True(process.HasExited);
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }

    private static ProcessStartInfo StartInfo(string script)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) info.ArgumentList.Add(argument);
        return info;
    }

    private static async Task WaitForFileAsync(string path)
    {
        var watch = Stopwatch.StartNew();
        while (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Probe did not start.");
            await Task.Delay(30);
        }
    }

    private static void AssertProcessExited(int pid)
    {
        try { using var process = Process.GetProcessById(pid); Assert.True(process.HasExited); }
        catch (ArgumentException) { }
    }

    private static object? Invoke(object window, string method, params object?[]? args) => window.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args?.Length > 0 ? args : [null, new RoutedEventArgs()]);
    private static async Task CloseAsync(Window window)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult(); window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static void RunUi(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async () => { try { await action(); } catch (Exception ex) { failure = ex; } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UI test did not finish.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private string CreatePdf()
    {
        var path = Path.Combine(_directory, "input.pdf"); using var document = new PdfDocument();
        document.AddPage(); document.AddPage(); document.AddPage(); document.Save(path); return path;
    }

    private sealed class CallbackProgress(Action<ConversionProgress> report) : IProgress<ConversionProgress> { public void Report(ConversionProgress value) => report(value); }
    private sealed class CleanupProbe : IConversionProvider
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), CleanupStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), AllowCleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public string Id => "pdf.cleanup-probe";
        public ConversionCapability Capability => new(Id, new HashSet<string> { "pdf" }, new HashSet<string> { "pdf" }, null, true);
        public ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AvailabilityResult(true));
        public ValidationResult Validate(ConversionRequest request) => ValidationResult.Valid;
        public async Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken token)
        {
            Interlocked.Increment(ref Calls); Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); return ConversionResult.Success("unused", TimeSpan.Zero, Id); }
            finally { CleanupStarted.TrySetResult(); await AllowCleanup.Task; }
        }
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
