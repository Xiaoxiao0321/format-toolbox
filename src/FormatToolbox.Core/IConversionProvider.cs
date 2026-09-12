namespace FormatToolbox.Core;

public interface IConversionProvider
{
    string Id { get; }
    ConversionCapability Capability { get; }
    ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default);
    ValidationResult Validate(ConversionRequest request);
    Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken);
}

public static class ErrorCodes
{
    public const string FileNotFound = "INPUT_FILE_NOT_FOUND";
    public const string UnsupportedFormat = "UNSUPPORTED_FORMAT";
    public const string DependencyMissing = "DEPENDENCY_MISSING";
    public const string OutputLocked = "OUTPUT_LOCKED";
    public const string AccessDenied = "ACCESS_DENIED";
    public const string ReadOnlyOutput = "READ_ONLY_OUTPUT";
    public const string DiskFull = "DISK_FULL";
    public const string InvalidOrEncrypted = "INVALID_OR_ENCRYPTED";
    public const string Cancelled = "CANCELLED";
    public const string Timeout = "TIMEOUT";
    public const string EngineFailure = "ENGINE_FAILURE";
}
