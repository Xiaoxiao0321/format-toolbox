using System.Diagnostics;
using System.Text.Json;
using FormatToolbox.Core;

namespace FormatToolbox.Infrastructure;

public sealed record WorkerExecutionResult(int ExitCode, string StandardOutput, string StandardError);

public static class WorkerProcessRunner
{
    public static async Task<WorkerExecutionResult> RunAsync(ProcessStartInfo startInfo, string sessionDirectory, TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = await Task.Run(() => NativeOutputSynchronization.StartProcess(startInfo, token)).ConfigureAwait(false);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutSource.Token);
        using var registration = linked.Token.Register(() =>
        {
            try { File.WriteAllText(Path.Combine(sessionDirectory, "cancel"), "cancel"); }
            catch (Exception ex) { DiagnosticLog.Write("worker.cancel-signal", ex); }
        });
        try
        {
            try { await process.WaitForExitAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                // Give the worker time to close its document and automation instance.
                await TerminateAsync(process, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                throw new TimeoutException("转换 Worker 超时，已终止并清理。");
            }
            token.ThrowIfCancellationRequested();
            return new(process.ExitCode, await stdout.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), await stderr.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
        }
        finally
        {
            try { await TerminateAsync(process, TimeSpan.Zero).ConfigureAwait(false); }
            catch (Exception ex) { DiagnosticLog.Write("worker.terminate", ex); }
            await CleanupAutomationAsync(Path.Combine(sessionDirectory, "automation.json")).ConfigureAwait(false);
            try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
        }
    }

    private static async Task TerminateAsync(Process process, TimeSpan grace)
    {
        if (process.HasExited) return;
        if (grace > TimeSpan.Zero)
            try { await process.WaitForExitAsync().WaitAsync(grace).ConfigureAwait(false); return; } catch (TimeoutException) { }
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) when (process.HasExited) { return; }
        catch (System.ComponentModel.Win32Exception) when (process.HasExited) { return; }
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    public static async Task CleanupAutomationAsync(string manifestPath)
    {
        if (!File.Exists(manifestPath)) return;
        try
        {
            var owned = JsonSerializer.Deserialize<OwnedAutomationProcess>(await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false));
            if (owned is null) return;
            using var process = Process.GetProcessById(owned.ProcessId);
            if (process.StartTime.ToUniversalTime().Ticks != owned.StartTimeUtcTicks) return;
            await TerminateAsync(process, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (ArgumentException) { } // Already exited.
        catch (Exception ex) { DiagnosticLog.Write("worker.automation-cleanup", ex); }
    }
}
