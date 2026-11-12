using FastDbExplorer.Application.Monitoring;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Tests;

public sealed class ProximityMatcherTests
{
    private static MapLayer Layer(string name, params (double Lat, double Lon)[] points)
    {
        var id = Guid.NewGuid();
        var mapPoints = points.Select((p, i) => new MapPoint(Guid.NewGuid(), $"{name}-{i}", p.Lat, p.Lon, id)).ToList();
        return new MapLayer(id, name, name + ".xlsx", mapPoints);
    }

    [Fact]
    public void Without_layers_every_row_passes_unchanged()
    {
        var row = new object?[] { 1, "a" };

        var matched = ProximityMatcher.Match(row, [], true, 0, 0, 100, default);

        Assert.Same(row, matched);
    }

    [Fact]
    public void A_row_next_to_a_point_matches_with_its_point_layer_and_distance()
    {
        var layers = new[] { Layer("Clinics", (35.7, 51.4)) };

        var matched = ProximityMatcher.Match(new object?[] { 1 }, layers, true, 35.7, 51.4, 100, default);

        Assert.NotNull(matched);
        Assert.Equal(1 + ProximityMatcher.ExtraColumns, matched.Length);
        Assert.Equal("Clinics-0", matched[^3]);
        Assert.Equal("Clinics", matched[^2]);
        Assert.Equal(0.0, Convert.ToDouble(matched[^1]), 3);
    }

    [Fact]
    public void A_row_too_far_from_every_point_does_not_match()
    {
        var layers = new[] { Layer("Clinics", (35.7, 51.4)) };

        // One degree of latitude is about 111 km, far beyond 100 metres.
        Assert.Null(ProximityMatcher.Match(new object?[] { 1 }, layers, true, 36.7, 51.4, 100, default));
    }

    [Fact]
    public void A_row_without_coordinates_never_matches_a_layer()
    {
        var layers = new[] { Layer("Clinics", (35.7, 51.4)) };

        Assert.Null(ProximityMatcher.Match(new object?[] { 1 }, layers, false, 35.7, 51.4, 100_000, default));
    }

    [Fact]
    public void The_closest_point_across_layers_names_the_match()
    {
        var layers = new[] { Layer("Far", (10, 10)), Layer("Near", (35.7, 51.4)) };

        var matched = ProximityMatcher.Match(new object?[] { 1 }, layers, true, 35.7, 51.4, 100_000, default);

        Assert.NotNull(matched);
        Assert.Equal("Near", matched[^2]);
    }
}
