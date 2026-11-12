using System.Text.Json;
using System.Text.Json.Serialization;
using FastDbExplorer.Application.Abstractions;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Infrastructure;

/// <summary>Named searches in %AppData%\FastDbExplorer\saved-searches.json. Only filters and profile ids, never passwords.</summary>
public sealed class JsonSavedSearchStore : ISavedSearchStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public JsonSavedSearchStore(string? path = null)
        => _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FastDbExplorer", "saved-searches.json");

    public async Task<IReadOnlyList<SavedSearch>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return [];
        try
        {
            await using var stream = File.OpenRead(_path);
            var items = await JsonSerializer.DeserializeAsync<List<SavedSearch>>(stream, Options, ct) ?? [];
            return items.Where(s => !string.IsNullOrWhiteSpace(s.Name)).OrderBy(s => s.Name, StringComparer.CurrentCulture).ToList();
        }
        catch (JsonException)
        {
            return []; // A damaged file must not block the app; the next save rewrites it.
        }
    }

    public async Task SaveAsync(SavedSearch search, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(search.Name)) throw new ArgumentException("A saved search needs a name.", nameof(search));
        var items = (await LoadAsync(ct)).Where(s => s.Id != search.Id).ToList();
        items.Add(search with { Name = search.Name.Trim(), UpdatedUtc = DateTime.UtcNow });
        await WriteAsync(items, ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var items = (await LoadAsync(ct)).Where(s => s.Id != id).ToList();
        await WriteAsync(items, ct);
    }

    private async Task WriteAsync(List<SavedSearch> items, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, items, Options, ct);
    }
}
