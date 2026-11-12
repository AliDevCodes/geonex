using FastDbExplorer.Application.Filters;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Tests;

public sealed class SavedFilterBookTests
{
    private static readonly SavedFilterScope Scope = new("sql-prod", "sales", "dbo", "orders");
    private static readonly IReadOnlyList<FilterCondition> Conditions =
        [new FilterCondition("amount", FilterOperator.EqualTo, "10")];

    private static SavedFilter Saved(string name, SavedFilterScope scope) => new(
        Guid.NewGuid(), name, scope.Server, scope.Database, scope.Schema, scope.Table,
        UseOr: false, Conditions, DateTime.MinValue);

    [Fact]
    public void A_filter_belongs_only_to_its_own_table_and_server_ignoring_case()
    {
        Assert.True(SavedFilterBook.BelongsTo(Saved("x", Scope), Scope with { Server = "SQL-PROD" }));
        Assert.False(SavedFilterBook.BelongsTo(Saved("x", Scope), Scope with { Table = "customers" }));
    }

    [Fact]
    public void Saving_under_an_existing_name_in_the_same_table_keeps_its_id()
    {
        var existing = Saved("Monthly", Scope);

        var prepared = SavedFilterBook.Prepare("monthly", Scope, [existing], true, Conditions, DateTime.UtcNow);

        Assert.Equal(existing.Id, prepared.Id);
        Assert.Equal("monthly", prepared.Name);
    }

    [Fact]
    public void The_same_name_in_another_table_gets_a_new_id()
    {
        var elsewhere = Saved("Monthly", Scope with { Table = "customers" });

        var prepared = SavedFilterBook.Prepare("Monthly", Scope, [elsewhere], false, Conditions, DateTime.UtcNow);

        Assert.NotEqual(elsewhere.Id, prepared.Id);
    }

    [Fact]
    public void The_prepared_filter_carries_the_scope_conditions_and_time()
    {
        var now = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);

        var prepared = SavedFilterBook.Prepare("Name", Scope, [], useOr: true, Conditions, now);

        Assert.Equal(("sql-prod", "sales", "dbo", "orders"),
            (prepared.Server, prepared.Database, prepared.Schema, prepared.Table));
        Assert.True(prepared.UseOr);
        Assert.Equal(Conditions, prepared.Conditions);
        Assert.Equal(now, prepared.UpdatedUtc);
    }
}
