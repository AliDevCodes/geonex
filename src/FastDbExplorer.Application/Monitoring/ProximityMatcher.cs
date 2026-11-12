using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Monitoring;

/// <summary>
/// The proximity rule of a monitor: a record matches when its nearest point, across every watched layer, is within the
/// distance. Pure: no database and no UI.
/// </summary>
public static class ProximityMatcher
{
    /// <summary>Values a match adds after the table's own columns: nearest point name, layer name, distance in metres.</summary>
    public const int ExtraColumns = 3;

    /// <summary>
    /// The row to report, or null when it does not match. Without layers every row passes unchanged. With layers, a row
    /// without usable coordinates (<paramref name="located"/> false) never matches.
    /// </summary>
    public static object?[]? Match(object?[] row, IReadOnlyList<MapLayer> layers, bool located, double latitude, double longitude,
        double maxDistanceMeters, CancellationToken ct)
    {
        if (layers.Count == 0) return row;
        if (!located) return null;

        // The nearest point is taken across every selected layer, so one row matches once
        // (a record close to two layers is reported with the closest of them).
        var nearest = FindNearest(layers, latitude, longitude, ct);
        if (nearest.DistanceMeters > maxDistanceMeters) return null;

        var matched = new object?[row.Length + ExtraColumns];
        Array.Copy(row, matched, row.Length);
        matched[^3] = nearest.Point.Name;
        matched[^2] = nearest.LayerName;
        matched[^1] = nearest.DistanceMeters;
        return matched;
    }

    private static (MapPoint Point, string LayerName, double DistanceMeters) FindNearest(
        IReadOnlyList<MapLayer> layers, double latitude, double longitude, CancellationToken ct)
    {
        MapPoint? nearest = null;
        var nearestLayer = "";
        var distance = double.PositiveInfinity;
        foreach (var layer in layers)
        {
            for (var i = 0; i < layer.Points.Count; i++)
            {
                if ((i & 0x3FF) == 0) ct.ThrowIfCancellationRequested();
                var point = layer.Points[i];
                var candidate = MonitorProximity.HaversineMeters(latitude, longitude, point.Latitude, point.Longitude);
                if (candidate >= distance) continue;
                nearest = point;
                nearestLayer = layer.Name;
                distance = candidate;
            }
        }
        return (nearest!, nearestLayer, distance);
    }
}
