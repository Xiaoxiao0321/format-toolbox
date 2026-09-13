using System.Diagnostics;
using System.Text.Json;
using FormatToolbox.Core;

namespace FormatToolbox.Infrastructure.Providers;

public sealed class WorkerConversionProvider(string id, IEnumerable<string> inputs, string dependency, string progId, string workerPath, TimeSpan? timeout = null) : IConversionProvider
{
    private readonly HashSet<string> _inputs = new(inputs, StringComparer.OrdinalIgnoreCase);
    public string Id => id;
    public ConversionCapability Capability { get; private set; } = new(id, new HashSet<string>(inputs, StringComparer.OrdinalIgnoreCase), new HashSet<string>(["pdf"], StringComparer.OrdinalIgnoreCase), dependency, false, $"未检测到 {dependency}");

    public ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var registration = OfficeComDetector.Find(progId);
            var available = registration is not null;
            var reason = available ? null : $"无法找到 {dependency} 的自动化组件。若已安装，请先打开该软件完成首次启动，再使用其安装程序修复组件注册后重试。";
            Capability = Capability with { IsAvailable = available, UnavailableReason = reason };
            return ValueTask.FromResult(new AvailabilityResult(available, registration is null ? null : $"{registration.ProgId} / {registration.View}", reason));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(Id + ".detect", ex);
            var reason = $"无法读取 {dependency} 的组件注册信息，请检查系统权限，或使用该软件安装程序修复。";
            Capability = Capability with { IsAvailable = false, UnavailableReason = reason };
            return ValueTask.FromResult(new AvailabilityResult(false, Reason: reason));
        }
    }

    public ValidationResult Validate(ConversionRequest request)
    {
        if (!File.Exists(request.InputPath)) return new(false, ErrorCodes.FileNotFound, "找不到输入文件。");
        var ext = Path.GetExtension(request.InputPath).TrimStart('.');
        return _inputs.Contains(ext) && request.TargetFormat.Equals("pdf", StringComparison.OrdinalIgnoreCase)
            ? ValidationResult.Valid : new(false, ErrorCodes.UnsupportedFormat, "该引擎不支持此转换。");
    }

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var availability = await CheckAvailabilityAsync(cancellationToken);
        if (!availability.IsAvailable) return ConversionResult.Failure(ErrorCodes.DependencyMissing, availability.Reason!, sw.Elapsed, Id);
        var output = OutputPathResolver.Resolve(request);
        try { OutputSafety.EnsureReady(output, new FileInfo(request.InputPath).Length * 3); }
        catch (Exception ex) { return OutputSafety.Failure(ex, sw.Elapsed, Id); }
        var temp = Path.Combine(Path.GetDirectoryName(output)!, $".{Guid.NewGuid():N}.pdf.tmp");
        var psi = new ProcessStartInfo(workerPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (!File.Exists(workerPath))
            return ConversionResult.Failure(ErrorCodes.DependencyMissing, "转换 Worker 未随应用安装，请重新安装或修复应用。", sw.Elapsed, Id);
        psi.ArgumentList.Add("--engine"); psi.ArgumentList.Add(Id);
        psi.ArgumentList.Add("--input"); psi.ArgumentList.Add(Path.GetFullPath(request.InputPath));
        psi.ArgumentList.Add("--output"); psi.ArgumentList.Add(temp);
        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            progress?.Report(new(20, $"正在调用 {dependency}"));
            using var timeoutSource = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(10));
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            using var registration = linkedSource.Token.Register(() => { try { process.Kill(true); } catch { } });
            try { await process.WaitForExitAsync(linkedSource.Token); }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                await EnsureTerminatedAsync(process);
                return ConversionResult.Failure(ErrorCodes.Timeout, $"{dependency} 转换超过 {(timeout ?? TimeSpan.FromMinutes(10)).TotalMinutes:0} 分钟，已终止 Worker。", sw.Elapsed, Id);
            }
            var responseText = await stdoutTask;
            var errorText = await stderrTask;
            var response = JsonSerializer.Deserialize<WorkerResponse>(responseText);
            if (process.ExitCode != 0 || response?.Success != true)
            {
                return ConversionResult.Failure(response?.ErrorCode ?? ErrorCodes.EngineFailure, response?.Message ?? errorText, sw.Elapsed, Id) with { HResult = response?.HResult };
            }
            File.Move(temp, output, request.OverwritePolicy == OverwritePolicy.Overwrite);
            progress?.Report(new(100, "转换完成"));
            return ConversionResult.Success(output, sw.Elapsed, response.Engine ?? Id, response.Warnings?.ToArray() ?? []);
        }
        catch (OperationCanceledException) { await EnsureTerminatedAsync(process); return ConversionResult.Failure(ErrorCodes.Cancelled, "任务已取消。", sw.Elapsed, Id); }
        catch (Exception ex) { DiagnosticLog.Write(Id, ex); return OutputSafety.Failure(ex, sw.Elapsed, Id); }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception ex) { DiagnosticLog.Write(Id + ".cleanup", ex); } }
    }

    private static async Task EnsureTerminatedAsync(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch { }
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
    }

    private sealed record WorkerResponse(bool Success, string? ErrorCode, string? Message, string? Engine, List<string>? Warnings, int? HResult = null);
}
