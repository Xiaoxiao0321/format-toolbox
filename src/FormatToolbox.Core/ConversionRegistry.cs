namespace FormatToolbox.Core;

public sealed class ConversionRegistry(IEnumerable<IConversionProvider> providers)
{
    private readonly IReadOnlyList<IConversionProvider> _providers = providers.ToArray();
    public IReadOnlyList<IConversionProvider> Providers => _providers;

    public IConversionProvider? Resolve(string inputPath, string targetFormat)
    {
        var source = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
        var target = targetFormat.TrimStart('.').ToLowerInvariant();
        return _providers.FirstOrDefault(p => p.Capability.InputFormats.Contains(source) && p.Capability.OutputFormats.Contains(target));
    }

    public IConversionProvider? Resolve(ConversionRequest request)
    {
        var matches = _providers.Where(p => p.Capability.InputFormats.Contains(Path.GetExtension(request.InputPath).TrimStart('.')) && p.Capability.OutputFormats.Contains(request.TargetFormat.TrimStart('.')));
        return request.Options is OcrOptions ? matches.FirstOrDefault(p => p.Id.StartsWith("ocr.")) : matches.FirstOrDefault(p => !p.Id.StartsWith("ocr."));
    }
}
