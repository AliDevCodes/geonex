namespace FastDbExplorer.Application.Map;

/// <summary>Guesses the coordinate and detail columns of a result preview from their names. Pure: no UI, no database.</summary>
public static class PreviewColumnDetector
{
    /// <summary>
    /// The latitude or longitude column of a preview: the preferred name when it is present (ignoring case), otherwise the
    /// column whose name looks most like a coordinate. Null when nothing looks like one.
    /// </summary>
    public static string? FindCoordinate(IReadOnlyList<string> columns, string? preferred, bool isLatitude)
    {
        if (preferred is not null)
            foreach (var column in columns)
                if (string.Equals(column, preferred, StringComparison.OrdinalIgnoreCase)) return column;

        var ranked = columns.Select((name, index) => (name, index, score: CoordinateNameScore(name, isLatitude)))
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.index)
            .FirstOrDefault();
        return ranked.score > 0 ? ranked.name : null;
    }

    /// <summary>
    /// The detail columns a preview selects by default: the two most useful names that are not coordinates, the
    /// earlier column winning a tie.
    /// </summary>
    public static HashSet<string> DefaultDetails(IReadOnlyList<string> columns, HashSet<string> coordinateNames) =>
        columns.Select((name, index) => (name, index))
            .Where(item => !coordinateNames.Contains(item.name))
            .OrderByDescending(item => DetailUsefulness(item.name))
            .ThenBy(item => item.index)
            .Take(2)
            .Select(item => item.name)
            .ToHashSet(StringComparer.Ordinal);

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int CoordinateNameScore(string name, bool isLatitude)
    {
        var normalized = Normalize(name);
        if (isLatitude)
        {
            if (normalized is "lat" or "latitude" or "y") return 5;
            if (normalized.Contains("latitude", StringComparison.Ordinal)) return 4;
            if (normalized.StartsWith("lat", StringComparison.Ordinal) || normalized.EndsWith("lat", StringComparison.Ordinal)) return 2;
        }
        else
        {
            if (normalized is "lon" or "lng" or "long" or "longitude" or "x") return 5;
            if (normalized.Contains("longitude", StringComparison.Ordinal)) return 4;
            if (normalized.StartsWith("lon", StringComparison.Ordinal) || normalized.EndsWith("lon", StringComparison.Ordinal)
                || normalized.StartsWith("lng", StringComparison.Ordinal) || normalized.EndsWith("lng", StringComparison.Ordinal)) return 2;
        }
        return 0;
    }

    private static int DetailUsefulness(string name)
    {
        var normalized = Normalize(name);
        if (normalized.Contains("name", StringComparison.Ordinal) || normalized.Contains("title", StringComparison.Ordinal)
            || normalized.Contains("address", StringComparison.Ordinal) || normalized.Contains("nearestpoint", StringComparison.Ordinal)) return 4;
        if (normalized.Contains("distance", StringComparison.Ordinal) || normalized.Contains("description", StringComparison.Ordinal)
            || normalized.Contains("type", StringComparison.Ordinal)) return 3;
        if (normalized.Contains("layer", StringComparison.Ordinal) || normalized.Contains("category", StringComparison.Ordinal)
            || normalized.Contains("status", StringComparison.Ordinal)) return 2;
        return 0;
    }
}
