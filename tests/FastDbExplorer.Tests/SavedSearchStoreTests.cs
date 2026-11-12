using FastDbExplorer.Domain;
using FastDbExplorer.Infrastructure;
using Xunit;

namespace FastDbExplorer.Tests;

public sealed class SavedSearchStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fdbx-saved-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Save_then_load_round_trips_without_secrets()
    {
        var store = new JsonSavedSearchStore(Path.Combine(_dir, "saved-searches.json"));
        var search = new SavedSearch(Guid.NewGuid(), "  کد ملی فروش  ",
            [new SearchTarget(Guid.NewGuid(), "sales-prod", "orders", "national_id", "nvarchar")],
            "0012345678", RelativeDays: 7, FromLocal: null, ToLocal: null, UpdatedUtc: DateTime.MinValue);

        await store.SaveAsync(search);
        var loaded = await store.LoadAsync();

        var item = Assert.Single(loaded);
        Assert.Equal("کد ملی فروش", item.Name);
        Assert.Equal("0012345678", item.SearchValue);
        Assert.True(item.UsesRelativeRange);
        Assert.Equal("sales-prod", item.Targets[0].Database);
        var text = File.ReadAllText(Path.Combine(_dir, "saved-searches.json"));
        Assert.DoesNotContain("Password", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Saving_the_same_id_replaces_it_and_remove_deletes_it()
    {
        var store = new JsonSavedSearchStore(Path.Combine(_dir, "saved-searches.json"));
        var id = Guid.NewGuid();
        var target = new SearchTarget(Guid.NewGuid(), "db", "t", "c", "int");
        await store.SaveAsync(new SavedSearch(id, "first", [target], "1", null, null, null, DateTime.MinValue));
        await store.SaveAsync(new SavedSearch(id, "second", [target], "2", null, null, null, DateTime.MinValue));

        var item = Assert.Single(await store.LoadAsync());
        Assert.Equal("second", item.Name);

        await store.RemoveAsync(id);
        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task A_damaged_file_loads_as_empty_instead_of_failing()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "saved-searches.json");
        File.WriteAllText(path, "{ not json");

        var store = new JsonSavedSearchStore(path);

        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task A_blank_name_is_rejected()
    {
        var store = new JsonSavedSearchStore(Path.Combine(_dir, "saved-searches.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(
            new SavedSearch(Guid.NewGuid(), "  ", [], "x", null, null, null, DateTime.MinValue)));
    }
}
