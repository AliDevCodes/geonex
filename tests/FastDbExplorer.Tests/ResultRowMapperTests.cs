using FastDbExplorer.Application.Map;

namespace FastDbExplorer.Tests;

public class ResultRowMapperTests
{
    private static readonly string[] Columns = ["Name", "Latitude", "Longitude", "City"];

    private static ResultRow Row(object? name, double? latitude, double? longitude, string city) => new(
        [name, latitude, longitude, city],
        [name?.ToString() ?? "", latitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
         longitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", city]);

    [Fact]
    public void A_row_with_valid_coordinates_becomes_a_point_named_by_its_first_text_cell()
    {
        var mapping = ResultRowMapper.Map(
            [Row("Cafe", 35.7, 51.4, "Tehran")], Columns, latitudeIndex: 1, longitudeIndex: 2, detailColumns: []);

        var point = Assert.Single(mapping.Points);
        Assert.Equal("Cafe", point.Name);
        Assert.Equal(35.7, point.Latitude);
        Assert.Equal(51.4, point.Longitude);
        Assert.Equal(0, mapping.Skipped);
    }

    [Fact]
    public void A_row_without_valid_coordinates_is_skipped_and_counted()
    {
        var mapping = ResultRowMapper.Map(
            [Row("Cafe", null, 51.4, "Tehran"), Row("Shop", 35.7, 51.4, "Tehran")],
            Columns, latitudeIndex: 1, longitudeIndex: 2, detailColumns: []);

        Assert.Single(mapping.Points);
        Assert.Equal(1, mapping.Skipped);
        Assert.Equal("Shop", mapping.Points[0].Name);
    }

    [Fact]
    public void Out_of_range_coordinate_values_are_skipped()
    {
        var mapping = ResultRowMapper.Map(
            [Row("Far", 999, 51.4, "Tehran")], Columns, latitudeIndex: 1, longitudeIndex: 2, detailColumns: []);

        Assert.Empty(mapping.Points);
        Assert.Equal(1, mapping.Skipped);
    }

    [Fact]
    public void Chosen_detail_columns_become_the_point_details()
    {
        var mapping = ResultRowMapper.Map(
            [Row("Cafe", 35.7, 51.4, "Tehran")], Columns, latitudeIndex: 1, longitudeIndex: 2, detailColumns: ["City"]);

        var details = Assert.Single(mapping.Points).Details;
        Assert.NotNull(details);
        Assert.Equal("Tehran", details["City"]);
    }

    [Fact]
    public void A_detail_column_missing_from_the_result_is_left_out()
    {
        var mapping = ResultRowMapper.Map(
            [Row("Cafe", 35.7, 51.4, "Tehran")], Columns, latitudeIndex: 1, longitudeIndex: 2, detailColumns: ["Unknown"]);

        Assert.Null(Assert.Single(mapping.Points).Details);
    }

    [Fact]
    public void A_coordinate_cell_is_never_used_as_the_name()
    {
        var mapping = ResultRowMapper.Map(
            [Row(null, 35.7, 51.4, "")], Columns, latitudeIndex: 1, longitudeIndex: 2, detailColumns: []);

        Assert.DoesNotContain("35.7", mapping.Points[0].Name);
        Assert.DoesNotContain("51.4", mapping.Points[0].Name);
    }
}
