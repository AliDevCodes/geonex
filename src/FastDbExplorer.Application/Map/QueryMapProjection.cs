using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Map;

/// <summary>Projects query rows and column metadata into map data without owning UI or database state.</summary>
public static class QueryMapProjection
{
    public static string? DetectCoordinateColumn(IReadOnlyList<ColumnInfo> columns, bool latitude)
    {
        var preferred = latitude
            ? new[] { "latitude", "lat", "y" }
            : new[] { "longitude", "long", "lng", "lon", "x" };
        var matches = new List<(string Name, int Index, int Rank)>();
        for (var index = 0; index < columns.Count; index++)
        {
            var name = columns[index].Name;
            var normalized = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            for (var rank = 0; rank < preferred.Length; rank++)
            {
                if (normalized == preferred[rank]) { matches.Add((name, index, rank)); break; }
                // Short aliases (x, y) must not match an unrelated name such as "key".
                if (preferred[rank].Length > 2 && normalized.Contains(preferred[rank], StringComparison.Ordinal))
                {
                    matches.Add((name, index, rank + preferred.Length));
                    break;
                }
            }
        }
        return matches.OrderBy(match => match.Rank).ThenBy(match => match.Index).Select(match => match.Name).FirstOrDefault();
    }

    public static IReadOnlyDictionary<string, object?> Values(IReadOnlyList<string> columns, IReadOnlyList<object?> row)
    {
        var result = new Dictionary<string, object?>(columns.Count, StringComparer.Ordinal);
        for (var index = 0; index < columns.Count; index++) result[columns[index]] = row[index];
        return result;
    }

    public static bool TryCreatePoint(
        IReadOnlyDictionary<string, object?> values, string latitudeColumn, string longitudeColumn,
        string? name, int rowNumber, IEnumerable<string> detailColumns, Func<object?, string> formatDetail,
        out QueryMapPoint point)
    {
        point = null!;
        if (!MapPointFactory.TryReadCoordinates(ValueOf(values, latitudeColumn), ValueOf(values, longitudeColumn),
                out var latitude, out var longitude))
            return false;

        var details = MapPointFactory.BuildDetails(detailColumns,
            column => values.TryGetValue(column, out var raw) ? formatDetail(raw) : null);
        point = MapPointFactory.Create(name, rowNumber, latitude, longitude, details);
        return true;
    }

    private static object? ValueOf(IReadOnlyDictionary<string, object?> values, string column) =>
        values.TryGetValue(column, out var value) ? value : null;
}
