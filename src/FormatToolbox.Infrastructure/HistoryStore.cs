using System.Text.Json;
using FormatToolbox.Core;

namespace FormatToolbox.Infrastructure;

public sealed record HistoryEntry(DateTimeOffset Timestamp, string InputPath, string TargetFormat, ConversionStatus Status, string? Error, IReadOnlyList<string>? OutputFiles = null, IReadOnlyList<string>? Warnings = null, ConversionRequest? Request = null);

public sealed class HistoryStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public HistoryStore(string? path = null) => _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FormatToolbox", "history.json");
    public async Task<IReadOnlyList<HistoryEntry>> LoadAsync() => File.Exists(_path)
        ? JsonSerializer.Deserialize<List<HistoryEntry>>(await File.ReadAllTextAsync(_path)) ?? [] : [];
    public async Task AppendAsync(HistoryEntry entry)
    {
        await _gate.WaitAsync();
        try
        {
            var entries = (await LoadAsync()).TakeLast(499).Append(entry).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }
        finally { _gate.Release(); }
    }
    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try { if (File.Exists(_path)) File.Delete(_path); }
        finally { _gate.Release(); }
    }
}
