using System.Collections.Concurrent;
using FormatToolbox.Core;

namespace FormatToolbox.Infrastructure;

public sealed record QueueItem(Guid Id, ConversionRequest Request)
{
    public ConversionStatus Status { get; internal set; } = ConversionStatus.Waiting;
    public ConversionResult? Result { get; internal set; }
    public double Progress { get; internal set; }
    public string Message { get; internal set; } = "等待中";
}

public sealed class ConversionQueue
{
    private readonly ConversionRegistry _registry;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokens = new();
    private readonly ConcurrentDictionary<Guid, Task> _tasks = new();
    private readonly SemaphoreSlim _officeGate = new(1), _cadGate = new(1), _localGate;
    private readonly object _lifecycleGate = new();
    private bool _stopping;
    public event EventHandler<QueueItem>? Changed;
    public bool HasActiveItems => !_tasks.IsEmpty;
    public int ActiveCount => _tokens.Count;

    public ConversionQueue(ConversionRegistry registry, int maxLocalConcurrency = 2)
    {
        if (maxLocalConcurrency < 1 || maxLocalConcurrency > 8) throw new ArgumentOutOfRangeException(nameof(maxLocalConcurrency));
        _registry = registry;
        _localGate = new(maxLocalConcurrency);
    }

    public QueueItem Enqueue(ConversionRequest request)
    {
        QueueItem item;
        lock (_lifecycleGate)
        {
            if (_stopping) throw new InvalidOperationException("队列正在退出，不能添加新任务。");
            item = new QueueItem(Guid.NewGuid(), request);
            var cts = new CancellationTokenSource();
            _tokens[item.Id] = cts;
            var task = RunAsync(item, cts);
            _tasks[item.Id] = task;
            _ = task.ContinueWith(completed => _tasks.TryRemove(item.Id, out _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        Notify(item);
        return item;
    }

    public void Cancel(Guid id) { if (_tokens.TryGetValue(id, out var cts)) TryCancel(cts); }
    private static void TryCancel(CancellationTokenSource cts) { try { cts.Cancel(); } catch (ObjectDisposedException) { } }

    public async Task StopAsync()
    {
        CancellationTokenSource[] tokens;
        lock (_lifecycleGate) { _stopping = true; tokens = _tokens.Values.ToArray(); }
        foreach (var cts in tokens) TryCancel(cts);
        await WaitForIdleAsync();
    }
    public async Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        while (!_tasks.IsEmpty)
        {
            var snapshot = _tasks.Values.ToArray();
            if (snapshot.Length == 0) break;
            await Task.WhenAll(snapshot).WaitAsync(cancellationToken);
        }
    }

    private async Task RunAsync(QueueItem item, CancellationTokenSource cts)
    {
        var token = cts.Token;
        IConversionProvider? provider = null;
        SemaphoreSlim? gate = null;
        var acquired = false;
        try
        {
            provider = _registry.Resolve(item.Request);
            if (provider is null) { Finish(item, ConversionResult.Failure(ErrorCodes.UnsupportedFormat, _registry.DescribeUnsupportedConversion(item.Request), TimeSpan.Zero, "router")); return; }
            gate = provider.Id.StartsWith("office") ? _officeGate : provider.Id.StartsWith("autocad") ? _cadGate : _localGate;
            item.Status = ConversionStatus.Checking; Notify(item);
            await gate.WaitAsync(token);
            acquired = true;
            item.Status = ConversionStatus.Processing; Notify(item);
            var progress = new QueueProgress(p => { item.Progress = p.Percent; item.Message = p.Message; Notify(item); });
            Finish(item, await Task.Run(() => provider.ConvertAsync(item.Request, progress, token)));
        }
        catch (OperationCanceledException) { Finish(item, ConversionResult.Failure(ErrorCodes.Cancelled, "任务已取消。", TimeSpan.Zero, provider?.Id ?? "router")); }
        catch (Exception ex) { DiagnosticLog.Write(provider?.Id ?? "queue", ex); Finish(item, ConversionResult.Failure(ErrorCodes.EngineFailure, "转换引擎发生未预期错误，请查看本地诊断日志。", TimeSpan.Zero, provider?.Id ?? "queue")); }
        finally { if (acquired) gate!.Release(); _tokens.TryRemove(item.Id, out _); cts.Dispose(); }
    }

    private void Finish(QueueItem item, ConversionResult result)
    {
        if (result.Status is ConversionStatus.Failed or ConversionStatus.PartialSucceeded)
            DiagnosticLog.WriteFailure(result.Engine, result.ErrorCode ?? ErrorCodes.EngineFailure, result.ErrorMessage, result.HResult);
        item.Result = result;
        item.Status = result.Status;
        item.Progress = result.Status == ConversionStatus.Succeeded ? 100 : item.Progress;
        item.Message = (result.Status == ConversionStatus.PartialSucceeded ? "部分成功；" : "") + (result.ErrorMessage is null
            ? result.Warnings.Count == 0 ? "完成" : "完成；警告：" + string.Join("；", result.Warnings)
            : $"[{result.ErrorCode}] {result.ErrorMessage}" + (result.Warnings.Count > 0 ? "；警告：" + string.Join("；", result.Warnings) : ""));
        Notify(item);
    }
    private void Notify(QueueItem item)
    {
        if (Changed is null) return;
        foreach (EventHandler<QueueItem> handler in Changed.GetInvocationList())
        {
            try { handler(this, item); }
            catch (Exception ex) { DiagnosticLog.Write("queue.event-handler", ex); }
        }
    }

    private sealed class QueueProgress(Action<ConversionProgress> report) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => report(value);
    }
}
