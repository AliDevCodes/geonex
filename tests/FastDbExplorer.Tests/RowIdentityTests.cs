using FastDbExplorer.Application.Search;

namespace FastDbExplorer.Tests;

public class RowIdentityTests
{
    private static readonly string[] Columns = ["Id", "Name", "Payload"];
    private static readonly string[] Identity = ["Id"];

    private static IReadOnlyDictionary<string, object?> Previous(params object?[] id) => new Dictionary<string, object?>
    {
        ["Id"] = id[0],
    };

    [Fact]
    public void The_same_rows_in_the_same_order_are_the_same()
    {
        var previous = new[] { Previous(1), Previous(2) };
        var current = new object?[][] { [1, "a", null], [2, "b", null] };

        Assert.True(RowIdentity.SameRows(previous, Columns, current, Identity));
    }

    [Fact]
    public void A_changed_identity_value_means_the_rows_moved()
    {
        var previous = new[] { Previous(1), Previous(2) };
        var current = new object?[][] { [1, "a", null], [3, "b", null] };

        Assert.False(RowIdentity.SameRows(previous, Columns, current, Identity));
    }

    [Fact]
    public void A_different_row_count_is_never_the_same()
    {
        var previous = new[] { Previous(1), Previous(2) };
        var current = new object?[][] { [1, "a", null] };

        Assert.False(RowIdentity.SameRows(previous, Columns, current, Identity));
    }

    [Fact]
    public void A_changed_non_identity_column_does_not_matter()
    {
        var previous = new[] { Previous(1) };
        var current = new object?[][] { [1, "changed", null] };

        Assert.True(RowIdentity.SameRows(previous, Columns, current, Identity));
    }

    [Fact]
    public void An_identity_column_missing_from_the_refreshed_page_means_not_the_same()
    {
        var previous = new[] { Previous(1) };
        var current = new object?[][] { [1, "a", null] };

        Assert.False(RowIdentity.SameRows(previous, ["Name", "Payload"], current, Identity));
    }

    [Fact]
    public void Byte_arrays_are_compared_by_content()
    {
        Assert.True(RowIdentity.ValuesEqual(new byte[] { 1, 2 }, new byte[] { 1, 2 }));
        Assert.False(RowIdentity.ValuesEqual(new byte[] { 1, 2 }, new byte[] { 1, 3 }));
    }

    [Fact]
    public void Null_equals_null_and_nothing_else()
    {
        Assert.True(RowIdentity.ValuesEqual(null, null));
        Assert.False(RowIdentity.ValuesEqual(null, 0));
    }
}
