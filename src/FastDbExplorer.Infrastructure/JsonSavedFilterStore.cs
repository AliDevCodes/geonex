using System.Text.Json;
using System.Text.Json.Serialization;
using FastDbExplorer.Application.Abstractions;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Infrastructure;

/// <summary>Named Explorer filters in %AppData%\FastDbExplorer\saved-filters.json. Conditions and table identity only.</summary>
public sealed class JsonSavedFilterStore : ISavedFilterStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public JsonSavedFilterStore(string? path = null)
        => _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FastDbExplorer", "saved-filters.json");

    public async Task<IReadOnlyList<SavedFilter>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return [];
        try
        {
            await using var stream = File.OpenRead(_path);
            var items = await JsonSerializer.DeserializeAsync<List<SavedFilter>>(stream, Options, ct) ?? [];
            return items
                .Where(f => !string.IsNullOrWhiteSpace(f.Name) && f.Conditions is not null)
                .OrderBy(f => f.Name, StringComparer.CurrentCulture)
                .ToList();
        }
        catch (JsonException)
        {
            return []; // A damaged file must not block the app; the next save rewrites it.
        }
    }

    public async Task SaveAsync(SavedFilter filter, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filter.Name)) throw new ArgumentException("A saved filter needs a name.", nameof(filter));
        var items = (await LoadAsync(ct)).Where(f => f.Id != filter.Id).ToList();
        items.Add(filter with { Name = filter.Name.Trim(), UpdatedUtc = DateTime.UtcNow });
        await WriteAsync(items, ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var items = (await LoadAsync(ct)).Where(f => f.Id != id).ToList();
        await WriteAsync(items, ct);
    }

    private async Task WriteAsync(List<SavedFilter> items, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, items, Options, ct);
    }
}
