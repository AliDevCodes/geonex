using FastDbExplorer.Domain;
using FastDbExplorer.Infrastructure;
using Xunit;

namespace FastDbExplorer.Tests;

public sealed class SavedFilterStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fdbx-filters-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static SavedFilter Make(Guid id, string name) => new(
        id, name, "sql-prod", "sales", "dbo", "orders", UseOr: true,
        [new FilterCondition("amount", FilterOperator.Between, "10", "20"),
         new FilterCondition("status", FilterOperator.IsNull)],
        DateTime.MinValue);

    [Fact]
    public async Task Save_then_load_keeps_conditions_and_table_identity()
    {
        var store = new JsonSavedFilterStore(Path.Combine(_dir, "saved-filters.json"));
        await store.SaveAsync(Make(Guid.NewGuid(), "  بزرگ‌تر از ده  "));

        var item = Assert.Single(await store.LoadAsync());
        Assert.Equal("بزرگ‌تر از ده", item.Name);
        Assert.Equal("orders", item.Table);
        Assert.True(item.UseOr);
        Assert.Equal(2, item.Conditions.Count);
        Assert.Equal(FilterOperator.Between, item.Conditions[0].Operator);
        Assert.Equal("20", item.Conditions[0].Value2);
    }

    [Fact]
    public async Task Saving_the_same_id_replaces_instead_of_duplicating()
    {
        var store = new JsonSavedFilterStore(Path.Combine(_dir, "saved-filters.json"));
        var id = Guid.NewGuid();
        await store.SaveAsync(Make(id, "first"));
        await store.SaveAsync(Make(id, "second"));

        var item = Assert.Single(await store.LoadAsync());
        Assert.Equal("second", item.Name);
    }

    [Fact]
    public async Task Remove_deletes_only_the_given_filter()
    {
        var store = new JsonSavedFilterStore(Path.Combine(_dir, "saved-filters.json"));
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        await store.SaveAsync(Make(keep, "keep"));
        await store.SaveAsync(Make(drop, "drop"));

        await store.RemoveAsync(drop);

        var item = Assert.Single(await store.LoadAsync());
        Assert.Equal(keep, item.Id);
    }

    [Fact]
    public async Task Damaged_file_loads_as_empty_list()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "saved-filters.json");
        await File.WriteAllTextAsync(path, "{ not json");

        var store = new JsonSavedFilterStore(path);

        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task Blank_name_is_rejected()
    {
        var store = new JsonSavedFilterStore(Path.Combine(_dir, "saved-filters.json"));

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(Make(Guid.NewGuid(), "   ")));
    }
}
