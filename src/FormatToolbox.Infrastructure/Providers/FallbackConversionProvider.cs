using FormatToolbox.Core;

namespace FormatToolbox.Infrastructure.Providers;

public sealed class FallbackConversionProvider(
    string id,
    IConversionProvider preferred,
    IConversionProvider fallback,
    string dependencyDescription) : IConversionProvider
{
    public string Id => id;
    public ConversionCapability Capability { get; private set; } = new(
        id,
        preferred.Capability.InputFormats,
        preferred.Capability.OutputFormats,
        dependencyDescription,
        false,
        $"未检测到 {dependencyDescription}");

    public async ValueTask<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var preferredStatus = await preferred.CheckAvailabilityAsync(cancellationToken);
        if (preferredStatus.IsAvailable)
        {
            Capability = Capability with { IsAvailable = true, UnavailableReason = null };
            return new(true, preferred.Id);
        }

        var fallbackStatus = await fallback.CheckAvailabilityAsync(cancellationToken);
        if (fallbackStatus.IsAvailable)
        {
            Capability = Capability with { IsAvailable = true, UnavailableReason = null };
            return new(true, fallback.Id);
        }

        var reason = $"未检测到 {dependencyDescription}";
        Capability = Capability with { IsAvailable = false, UnavailableReason = reason };
        return new(false, Reason: reason);
    }

    public ValidationResult Validate(ConversionRequest request)
    {
        var first = preferred.Validate(request);
        return first.IsValid ? first : fallback.Validate(request);
    }

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
    {
        var preferredStatus = await preferred.CheckAvailabilityAsync(cancellationToken);
        if (preferredStatus.IsAvailable) return await preferred.ConvertAsync(request, progress, cancellationToken);

        var fallbackStatus = await fallback.CheckAvailabilityAsync(cancellationToken);
        if (fallbackStatus.IsAvailable)
        {
            progress?.Report(new(5, "Microsoft Office 不可用，已切换到 WPS。"));
            return await fallback.ConvertAsync(request, progress, cancellationToken);
        }

        return ConversionResult.Failure(ErrorCodes.DependencyMissing, $"未检测到 {dependencyDescription}。请安装其中一个桌面组件。", TimeSpan.Zero, Id);
    }
}
