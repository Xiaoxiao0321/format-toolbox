using FormatToolbox.Core;

namespace FormatToolbox.Infrastructure.Providers;

public sealed class PdfSplitProvider : IConversionProvider
{
    public string Id => "pdf.split";
    public ConversionCapability Capability => new(Id, new HashSet<string>(["pdf"], StringComparer.OrdinalIgnoreCase), new HashSet<string>(["pdf"], StringComparer.OrdinalIgnoreCase), "PDFsharp", true);
    public ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AvailabilityResult(true));
    public ValidationResult Validate(ConversionRequest request)
    {
        if (!File.Exists(request.InputPath)) return new(false, ErrorCodes.FileNotFound, "找不到 PDF 文件。");
        return request.Options is PdfSplitOptions && request.TargetFormat.Equals("pdf", StringComparison.OrdinalIgnoreCase)
            ? ValidationResult.Valid : new(false, ErrorCodes.UnsupportedFormat, "PDF 拆分需要有效的 PDF 输入和拆分参数。");
    }
    public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        return validation.IsValid ? new PdfSplitService().SplitAsync(request, progress, cancellationToken)
            : Task.FromResult(ConversionResult.Failure(validation.ErrorCode!, validation.Message!, TimeSpan.Zero, Id));
    }
}
