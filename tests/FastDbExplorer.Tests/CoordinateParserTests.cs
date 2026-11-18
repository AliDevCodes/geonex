using FastDbExplorer.Application.Monitoring;

namespace FastDbExplorer.Tests;

public sealed class CoordinateParserTests
{
    [Fact]
    public void Persian_digits_with_arabic_or_comma_separators_read_as_coordinates()
    {
        // ۳۵٫۷ (Arabic decimal separator) and ۵۱,۴ (ASCII comma), typed on a Persian keyboard.
        var row = new object?[] { "۳۵٫۷", "۵۱,۴" };

        Assert.True(CoordinateParser.TryReadCoordinates(row, 0, 1, out var latitude, out var longitude));
        Assert.Equal(35.7, latitude, 6);
        Assert.Equal(51.4, longitude, 6);
    }

    [Fact]
    public void Null_and_out_of_range_values_are_not_coordinates()
    {
        Assert.False(CoordinateParser.TryReadCoordinates(new object?[] { null, 51.0 }, 0, 1, out _, out _));
        Assert.False(CoordinateParser.TryReadCoordinates(new object?[] { 95.0, 51.0 }, 0, 1, out _, out _));
    }
}
