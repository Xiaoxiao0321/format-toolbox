namespace FormatToolbox.Core;

public enum ConversionStatus { Waiting, Checking, Processing, Succeeded, Failed, Cancelled }
public enum OverwritePolicy { Rename, Overwrite, Fail }

public sealed record ConversionRequest(
    string InputPath,
    string TargetFormat,
    string? OutputDirectory = null,
    OverwritePolicy OverwritePolicy = OverwritePolicy.Rename,
    ConversionOptions? Options = null,
    string? OutputFileName = null);

public abstract record ConversionOptions;
public sealed record ImageOptions(int Quality = 90, double Dpi = 96) : ConversionOptions;
public sealed record PdfRenderOptions(string? PageRange = null, int Dpi = 144, int JpegQuality = 90) : ConversionOptions;
public sealed record OfficeOptions(bool FitToPageWhenUnset = true) : ConversionOptions;
public sealed record PdfOptions(
    string? PageRange = null,
    int CompressionLevel = 6,
    string? Watermark = null,
    int RotationDegrees = 0,
    IReadOnlyList<string>? AdditionalInputs = null,
    bool RasterizeForCompression = false,
    int CompressionDpi = 144,
    int CompressionJpegQuality = 75,
    IReadOnlyList<int>? PageOrder = null) : ConversionOptions;
public sealed record OcrOptions(string Languages = "chi_sim+eng", string? PageRange = null, int Dpi = 300, bool Grayscale = false) : ConversionOptions;
public sealed record DwgOptions(bool IncludeModel = false) : ConversionOptions;

public sealed record ConversionResult(
    ConversionStatus Status,
    IReadOnlyList<string> OutputFiles,
    IReadOnlyList<string> Warnings,
    string? ErrorCode,
    string? ErrorMessage,
    TimeSpan Duration,
    string Engine)
{
    public int? HResult { get; init; }
    public static ConversionResult Success(string output, TimeSpan elapsed, string engine, params string[] warnings) =>
        new(ConversionStatus.Succeeded, [output], warnings, null, null, elapsed, engine);
    public static ConversionResult Failure(string code, string message, TimeSpan elapsed, string engine) =>
        new(code == ErrorCodes.Cancelled ? ConversionStatus.Cancelled : ConversionStatus.Failed, [], [], code, message, elapsed, engine);
}

public sealed record ConversionCapability(
    string ProviderId,
    IReadOnlySet<string> InputFormats,
    IReadOnlySet<string> OutputFormats,
    string? Dependency,
    bool IsAvailable,
    string? UnavailableReason = null);

public sealed record AvailabilityResult(bool IsAvailable, string? Version = null, string? Reason = null);
public sealed record ValidationResult(bool IsValid, string? ErrorCode = null, string? Message = null)
{
    public static readonly ValidationResult Valid = new(true);
}

public sealed record ConversionProgress(double Percent, string Message);
