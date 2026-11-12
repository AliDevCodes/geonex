using FastDbExplorer.Application.Map;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Tests;

public sealed class QueryMapProjectionTests
{
    [Fact]
    public void Coordinate_detection_prefers_exact_aliases_over_earlier_partial_matches()
    {
        ColumnInfo[] columns =
        [
            new("LatitudeEstimate", "float", 0),
            new("Key", "int", 0),
            new("Latitude", "float", 0),
            new("X", "float", 0),
            new("LongitudeValue", "float", 0),
            new("Lng", "float", 0)
        ];

        Assert.Equal("Latitude", QueryMapProjection.DetectCoordinateColumn(columns, latitude: true));
        Assert.Equal("Lng", QueryMapProjection.DetectCoordinateColumn(columns, latitude: false));
    }

    [Fact]
    public void Short_coordinate_aliases_must_match_the_entire_column_name()
    {
        ColumnInfo[] columns = [new("Key", "int", 0), new("Proxy", "float", 0)];

        Assert.Null(QueryMapProjection.DetectCoordinateColumn(columns, latitude: true));
        Assert.Null(QueryMapProjection.DetectCoordinateColumn(columns, latitude: false));
    }

    [Fact]
    public void Values_keep_column_names_and_raw_values_for_map_only_fields()
    {
        var values = QueryMapProjection.Values(["Lat", "Lon", "Details"], ["۳۵.۷", 51.4, null]);

        Assert.Equal("۳۵.۷", values["Lat"]);
        Assert.Equal(51.4, values["Lon"]);
        Assert.Null(values["Details"]);
    }

    [Fact]
    public void Point_keeps_display_name_and_formats_selected_details()
    {
        var values = QueryMapProjection.Values(["Lat", "Lon", "Info", "Hidden"],
            ["۳۵.۷", "۵۱.۴", new DateTime(2026, 11, 12), "private"]);

        var created = QueryMapProjection.TryCreatePoint(values, "Lat", "Lon", "نمایش", 9,
            ["Info"], value => value is DateTime ? "۱۴۰۵/۰۸/۲۱" : value?.ToString() ?? "NULL", out var point);

        Assert.True(created);
        Assert.Equal("نمایش", point.Name);
        Assert.Equal(35.7, point.Latitude, 6);
        Assert.Equal(51.4, point.Longitude, 6);
        Assert.Equal("۱۴۰۵/۰۸/۲۱", point.Details!["Info"]);
        Assert.DoesNotContain("Hidden", point.Details.Keys);
    }

    [Fact]
    public void Invalid_coordinates_skip_point_and_blank_name_uses_row_number()
    {
        var invalid = QueryMapProjection.Values(["Lat", "Lon"], [91.0, 51.0]);
        Assert.False(QueryMapProjection.TryCreatePoint(invalid, "Lat", "Lon", "", 3,
            [], _ => "", out _));

        var valid = QueryMapProjection.Values(["Lat", "Lon"], [35.0, 51.0]);
        Assert.True(QueryMapProjection.TryCreatePoint(valid, "Lat", "Lon", null, 3,
            [], _ => "", out var point));
        Assert.Equal("Row 3", point.Name);
    }
}
