using FastDbExplorer.Application.Map;

namespace FastDbExplorer.Tests;

public class PreviewColumnDetectorTests
{
    [Fact]
    public void Finds_latitude_and_longitude_by_their_names()
    {
        var columns = new[] { "Name", "Latitude", "Longitude" };

        Assert.Equal("Latitude", PreviewColumnDetector.FindCoordinate(columns, null, isLatitude: true));
        Assert.Equal("Longitude", PreviewColumnDetector.FindCoordinate(columns, null, isLatitude: false));
    }

    [Fact]
    public void A_preferred_name_wins_regardless_of_case()
    {
        var columns = new[] { "lat", "Latitude" };

        Assert.Equal("Latitude", PreviewColumnDetector.FindCoordinate(columns, "LATITUDE", isLatitude: true));
    }

    [Fact]
    public void A_name_that_ends_with_lon_is_still_a_longitude_candidate()
    {
        var columns = new[] { "Name", "GeoLon" };

        Assert.Equal("GeoLon", PreviewColumnDetector.FindCoordinate(columns, null, isLatitude: false));
    }

    [Fact]
    public void No_coordinate_like_name_gives_null()
    {
        var columns = new[] { "Name", "Status" };

        Assert.Null(PreviewColumnDetector.FindCoordinate(columns, null, isLatitude: true));
    }

    [Fact]
    public void Default_details_are_the_two_most_useful_non_coordinate_columns()
    {
        var columns = new[] { "Id", "Latitude", "Name", "Status", "Longitude" };
        var coordinates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Latitude", "Longitude" };

        var defaults = PreviewColumnDetector.DefaultDetails(columns, coordinates);

        Assert.Equal(new HashSet<string> { "Name", "Status" }, defaults);
    }

    [Fact]
    public void Default_details_never_pick_a_coordinate_even_when_its_name_is_useful()
    {
        var columns = new[] { "Address", "Latitude", "Longitude" };
        var coordinates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Address", "Latitude", "Longitude" };

        Assert.Empty(PreviewColumnDetector.DefaultDetails(columns, coordinates));
    }

    [Fact]
    public void When_usefulness_ties_the_earlier_column_is_chosen()
    {
        var columns = new[] { "Code", "Other", "Third" };
        var coordinates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var defaults = PreviewColumnDetector.DefaultDetails(columns, coordinates);

        Assert.Equal(new HashSet<string> { "Code", "Other" }, defaults);
    }
}
