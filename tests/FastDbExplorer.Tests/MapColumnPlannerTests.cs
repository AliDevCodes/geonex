using FastDbExplorer.Application.Map;

namespace FastDbExplorer.Tests;

public class MapColumnPlannerTests
{
    private static readonly string[] Coordinates = ["Latitude", "Longitude"];

    [Fact]
    public void A_grid_query_reads_coordinates_details_and_the_primary_key_in_that_order()
    {
        var columns = MapColumnPlanner.RequiredForGrid(
            Coordinates, "Latitude", "Longitude", ["Name"], usesPrimaryKey: true, keyColumns: ["Id"], orderColumn: null);

        Assert.Equal(new[] { "Latitude", "Longitude", "Name", "Id" }, columns);
    }

    [Fact]
    public void Without_a_primary_key_the_chosen_order_column_is_read_instead()
    {
        var columns = MapColumnPlanner.RequiredForGrid(
            Coordinates, "Latitude", "Longitude", [], usesPrimaryKey: false, keyColumns: ["Id"], orderColumn: "Created");

        Assert.Equal(new[] { "Latitude", "Longitude", "Created" }, columns);
        Assert.DoesNotContain("Id", columns);
    }

    [Fact]
    public void A_coordinate_that_is_not_a_coordinate_column_is_left_out()
    {
        var columns = MapColumnPlanner.RequiredForGrid(
            Coordinates, "Latitude", "Other", [], usesPrimaryKey: false, keyColumns: [], orderColumn: null);

        Assert.Equal(new[] { "Latitude" }, columns);
    }

    [Fact]
    public void A_column_listed_twice_is_read_once()
    {
        var columns = MapColumnPlanner.RequiredForGrid(
            Coordinates, "Latitude", "Longitude", ["Latitude", "Name"], usesPrimaryKey: false, keyColumns: [], orderColumn: "Name");

        Assert.Equal(new[] { "Latitude", "Longitude", "Name" }, columns);
    }

    [Fact]
    public void All_rows_query_takes_the_two_coordinates_the_name_and_capped_details()
    {
        var columns = MapColumnPlanner.ForAllRows(
            "Latitude", "Longitude", "Name", ["City", "Phone", "Email"], maxDetailColumns: 2);

        Assert.Equal(new[] { "Latitude", "Longitude", "Name", "City", "Phone" }, columns);
    }

    [Fact]
    public void All_rows_query_works_without_a_name_column()
    {
        var columns = MapColumnPlanner.ForAllRows("Latitude", "Longitude", null, [], maxDetailColumns: 5);

        Assert.Equal(new[] { "Latitude", "Longitude" }, columns);
    }

    [Fact]
    public void All_rows_query_drops_a_detail_column_that_repeats_a_coordinate()
    {
        var columns = MapColumnPlanner.ForAllRows("Latitude", "Longitude", "Name", ["Latitude"], maxDetailColumns: 5);

        Assert.Equal(new[] { "Latitude", "Longitude", "Name" }, columns);
    }
}
