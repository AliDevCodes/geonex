using FastDbExplorer.Application.Search;
using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Search;

namespace FastDbExplorer.Tests;

public class TableColumnProfileTests
{
    private static ColumnInfo Col(string name, string type = "nvarchar", int key = 0) => new(name, type, key);

    [Fact]
    public void Primary_key_columns_are_ordered_by_key_position_and_enable_keyset_paging()
    {
        var profile = TableColumnProfile.From(
            [Col("B", "int", 2), Col("A", "int", 1), Col("Name")], "A", null, null);

        Assert.Equal(new[] { "A", "B" }, profile.KeyColumnNames);
        Assert.True(profile.UsesPrimaryKey);
        Assert.Equal("A, B", profile.KeyDescription);
    }

    [Fact]
    public void A_table_without_a_key_does_not_use_the_primary_key()
    {
        var profile = TableColumnProfile.From([Col("Name"), Col("City")], "Name", null, null);

        Assert.Empty(profile.KeyColumnNames);
        Assert.False(profile.UsesPrimaryKey);
    }

    [Fact]
    public void A_key_made_of_an_unfilterable_column_cannot_be_used_for_paging()
    {
        var profile = TableColumnProfile.From([Col("Id", "xml", 1), Col("Name")], "Name", null, null);

        Assert.False(profile.UsesPrimaryKey);
    }

    [Fact]
    public void Coordinate_columns_are_detected_from_their_names()
    {
        var profile = TableColumnProfile.From(
            [Col("Name"), Col("Latitude", "float"), Col("Longitude", "float")], "Name", null, null);

        Assert.Equal("Latitude", profile.LatitudeColumn);
        Assert.Equal("Longitude", profile.LongitudeColumn);
    }

    [Fact]
    public void Coordinate_columns_the_user_already_chose_are_kept()
    {
        var profile = TableColumnProfile.From(
            [Col("Lat", "float"), Col("Lon", "float"), Col("Y", "float"), Col("X", "float")], "Lat", "Y", "X");

        Assert.Equal("Y", profile.LatitudeColumn);
        Assert.Equal("X", profile.LongitudeColumn);
    }

    [Fact]
    public void Default_details_never_include_the_display_column_or_the_coordinates()
    {
        var profile = TableColumnProfile.From(
            [Col("Name"), Col("Latitude", "float"), Col("Longitude", "float"), Col("City"), Col("Notes")], "Name", null, null);

        Assert.DoesNotContain("Name", profile.DefaultDetailColumns);
        Assert.DoesNotContain("Latitude", profile.DefaultDetailColumns);
        Assert.DoesNotContain("Longitude", profile.DefaultDetailColumns);
        Assert.Equal(2, profile.DefaultDetailColumns.Count);
    }

    [Fact]
    public void Only_filterable_columns_are_offered()
    {
        var profile = TableColumnProfile.From([Col("Name"), Col("Blob", "varbinary")], "Name", null, null);

        Assert.Equal(new[] { "Name" }, profile.FilterableColumns);
    }
}
