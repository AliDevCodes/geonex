using FastDbExplorer.Application.Map;

namespace FastDbExplorer.Tests;

public sealed class MapPointFactoryTests
{
    [Fact]
    public void Coordinates_accept_persian_digits()
    {
        Assert.True(MapPointFactory.TryReadCoordinates("۳۵.۷", "۵۱", out var latitude, out var longitude));
        Assert.Equal(35.7, latitude, 6);
        Assert.Equal(51, longitude, 6);
    }

    [Fact]
    public void Coordinates_outside_the_valid_range_are_rejected()
    {
        Assert.False(MapPointFactory.TryReadCoordinates(91.0, 10.0, out _, out _));
        Assert.False(MapPointFactory.TryReadCoordinates(10.0, 181.0, out _, out _));
    }

    [Theory]
    [InlineData(null, 10.0)]
    [InlineData(10.0, "abc")]
    [InlineData("10.0", null)]
    public void Missing_or_non_numeric_coordinates_are_rejected(object? latitude, object? longitude)
    {
        Assert.False(MapPointFactory.TryReadCoordinates(latitude, longitude, out _, out _));
    }

    [Fact]
    public void A_blank_name_becomes_the_row_number()
    {
        var point = MapPointFactory.Create("   ", 7, 35, 51, null);
        Assert.Equal("Row 7", point.Name);
    }

    [Fact]
    public void Long_names_are_shortened_with_an_ellipsis()
    {
        var point = MapPointFactory.Create(new string('a', 150), 1, 35, 51, null);
        Assert.Equal(MapPointFactory.MaxNameLength, point.Name.Length);
        Assert.EndsWith("…", point.Name);
    }

    [Fact]
    public void Shortening_never_splits_a_surrogate_pair()
    {
        var value = new string('a', 98) + "😀" + "xxx";
        Assert.Equal(new string('a', 98) + "…", MapPointFactory.ShortenText(value, 100));
    }

    [Fact]
    public void Details_keep_the_column_order_and_skip_missing_values()
    {
        var texts = new Dictionary<string, string?> { ["b"] = "2", ["c"] = null, ["a"] = "1" };
        var details = MapPointFactory.BuildDetails(new[] { "a", "b", "c" }, column => texts.GetValueOrDefault(column));

        Assert.NotNull(details);
        Assert.Equal(new[] { "a", "b" }, details.Keys);
    }

    [Fact]
    public void At_most_eight_detail_columns_are_read()
    {
        var columns = Enumerable.Range(1, 10).Select(i => "c" + i).ToList();
        var details = MapPointFactory.BuildDetails(columns, _ => "v");

        Assert.NotNull(details);
        Assert.Equal(MapPointFactory.MaxDetailColumns, details.Count);
        Assert.DoesNotContain("c9", details.Keys);
    }

    [Fact]
    public void Detail_values_are_capped_and_no_text_gives_null()
    {
        var details = MapPointFactory.BuildDetails(new[] { "a" }, _ => new string('x', 500));

        Assert.NotNull(details);
        Assert.Equal(MapPointFactory.MaxDetailValueLength, details["a"].Length);
        Assert.Null(MapPointFactory.BuildDetails(new[] { "a" }, _ => null));
    }

    [Fact]
    public void Booleans_and_dates_are_not_coordinates()
    {
        Assert.False(MapPointFactory.TryReadCoordinates(true, 10.0, out _, out _));
        Assert.False(MapPointFactory.TryReadCoordinates(10.0, new DateTime(2026, 1, 1), out _, out _));
    }

    [Fact]
    public void The_arabic_decimal_separator_is_accepted()
    {
        Assert.True(MapPointFactory.TryReadCoordinates("۳۵\u066B۷", 51.0, out var latitude, out _));
        Assert.Equal(35.7, latitude, 6);
    }
}
