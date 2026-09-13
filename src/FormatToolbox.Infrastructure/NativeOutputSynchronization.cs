using System.Diagnostics;

namespace FormatToolbox.Infrastructure;

public static class NativeOutputSynchronization
{
    private static readonly ReaderWriterLockSlim Gate = new();

    // Tesseract's native output handles can be inherited by a new Windows process.
    // Keep process creation outside the lifetime of those handles; OCR jobs may still run together.
    public static IDisposable EnterOcrOutput(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        while (!Gate.TryEnterReadLock(50)) token.ThrowIfCancellationRequested();
        if (token.IsCancellationRequested) { Gate.ExitReadLock(); token.ThrowIfCancellationRequested(); }
        return new OutputScope();
    }

    public static Process StartProcess(ProcessStartInfo info, CancellationToken token)
    {
        while (!Gate.TryEnterWriteLock(50)) token.ThrowIfCancellationRequested();
        try { token.ThrowIfCancellationRequested(); return Process.Start(info) ?? throw new IOException("无法启动转换进程。"); }
        finally { Gate.ExitWriteLock(); }
    }

    private sealed class OutputScope : IDisposable { public void Dispose() => Gate.ExitReadLock(); }
}
