namespace FormatToolbox.Core;

public sealed record WorkerResponse(bool Success, string? ErrorCode, string? Message, string? Engine, IReadOnlyList<string>? Warnings, int? HResult = null, bool PartialSuccess = false)
{
    public ConversionResult ToResult(string output, TimeSpan duration, string fallbackEngine)
    {
        var engine = Engine ?? fallbackEngine;
        if (!Success) return ConversionResult.Failure(ErrorCode ?? ErrorCodes.EngineFailure, Message ?? "Worker 转换失败。", duration, engine) with { HResult = HResult, Warnings = Warnings ?? [] };
        var result = ConversionResult.Success(output, duration, engine, Warnings?.ToArray() ?? []);
        return PartialSuccess ? result with { Status = ConversionStatus.PartialSucceeded, ErrorCode = ErrorCodes.PartialOutput, ErrorMessage = Message ?? "部分布局未能输出，请检查布局警告。" } : result;
    }
}
