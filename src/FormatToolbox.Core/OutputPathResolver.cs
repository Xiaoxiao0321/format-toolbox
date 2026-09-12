namespace FormatToolbox.Core;

public static class OutputPathResolver
{
    public static string Resolve(ConversionRequest request)
    {
        var input = Path.GetFullPath(request.InputPath);
        var directory = request.OutputDirectory is { Length: > 0 }
            ? Path.GetFullPath(request.OutputDirectory)
            : Path.Combine(Path.GetDirectoryName(input)!, "转换结果");
        var safeName = string.IsNullOrWhiteSpace(request.OutputFileName) ? Path.GetFileNameWithoutExtension(input) : Path.GetFileNameWithoutExtension(request.OutputFileName.Trim());
        if (safeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new IOException("输出文件名包含无效字符。");
        var desired = Path.Combine(directory, safeName + "." + request.TargetFormat.TrimStart('.').ToLowerInvariant());
        if (request.OverwritePolicy == OverwritePolicy.Overwrite || !File.Exists(desired)) return desired;
        if (request.OverwritePolicy == OverwritePolicy.Fail) throw new IOException("输出文件已存在。");
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(directory, $"{safeName} ({i}).{request.TargetFormat.TrimStart('.').ToLowerInvariant()}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
