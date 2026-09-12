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

    public string DescribeUnsupportedConversion(ConversionRequest request)
    {
        var source = Path.GetExtension(request.InputPath).TrimStart('.').ToLowerInvariant();
        var target = request.TargetFormat.TrimStart('.').ToLowerInvariant();
        var sourceLabel = string.IsNullOrEmpty(source) ? "无扩展名文件" : source.ToUpperInvariant();
        var targetLabel = request.Options is OcrOptions ? "可搜索 PDF" : target.ToUpperInvariant();
        var message = $"当前不支持 {sourceLabel} 直接转为 {targetLabel}。";
        if (request.Options is not OcrOptions && target is "png" or "jpg" or "jpeg" && Resolve(request.InputPath, "pdf") is not null && Resolve("中间文件.pdf", target) is not null)
            return message + (source == "dwg"
                ? "请先使用“DWG 转 PDF”（需要完整版 AutoCAD），再使用“PDF 导出图片”。"
                : "请先使用“文档 / 表格 / 演示转 PDF”（需要桌面版 Microsoft Office 或 WPS），再使用“PDF 导出图片”。");
        var outputs = _providers.Where(p => p.Capability.InputFormats.Contains(source) && !p.Id.StartsWith("ocr."))
            .SelectMany(p => p.Capability.OutputFormats).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => x.ToUpperInvariant()).ToArray();
        return message + (outputs.Length == 0 ? "请检查文件扩展名，并添加本工具支持的文件。" : $"请将目标格式改为：{string.Join("、", outputs)}。");
    }
}
