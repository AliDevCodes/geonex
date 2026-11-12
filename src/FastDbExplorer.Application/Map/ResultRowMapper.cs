using System;
using System.Collections.Generic;
using System.Linq;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Map;

/// <summary>One result row: its typed values (for coordinates) and the display text of each cell (for names and details).</summary>
public sealed record ResultRow(IReadOnlyList<object?> Values, IReadOnlyList<string> Cells);

/// <summary>The map points made from the rows that had usable coordinates, and how many rows were skipped.</summary>
public sealed record ResultRowMapping(IReadOnlyList<QueryMapPoint> Points, int Skipped);

/// <summary>Turns selected result rows into map points. Pure: no UI, no database.</summary>
public static class ResultRowMapper
{
    /// <summary>True when the row's latitude and longitude cells hold valid coordinates.</summary>
    public static bool TryGetCoordinates(
        IReadOnlyList<object?> values, int latitudeIndex, int longitudeIndex, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        if (latitudeIndex >= values.Count || longitudeIndex >= values.Count) return false;
        return MapPointFactory.TryReadCoordinates(values[latitudeIndex], values[longitudeIndex], out latitude, out longitude);
    }

    /// <summary>
    /// One point per row with valid coordinates, named by its first text cell that is not a coordinate, with the chosen
    /// detail columns. Rows without valid coordinates are counted as skipped.
    /// </summary>
    public static ResultRowMapping Map(
        IReadOnlyList<ResultRow> rows, IReadOnlyList<string> columns,
        int latitudeIndex, int longitudeIndex, IReadOnlyList<string> detailColumns)
    {
        var columnIndexes = FirstIndexes(columns);
        var points = new List<QueryMapPoint>(rows.Count);
        var skipped = 0;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (!TryGetCoordinates(row.Values, latitudeIndex, longitudeIndex, out var latitude, out var longitude))
            {
                skipped++;
                continue;
            }

            var name = FirstTextCell(row, latitudeIndex, longitudeIndex);
            var details = MapPointFactory.BuildDetails(detailColumns, column => CellOf(row, columnIndexes, column));
            points.Add(MapPointFactory.Create(name, rowIndex + 1, latitude, longitude, details));
        }
        return new ResultRowMapping(points, skipped);
    }

    /// <summary>The first non-empty text cell that is not a coordinate. Null when there is none.</summary>
    private static string? FirstTextCell(ResultRow row, int latitudeIndex, int longitudeIndex)
    {
        for (var index = 0; index < row.Values.Count; index++)
        {
            if (index == latitudeIndex || index == longitudeIndex || row.Values[index] is null or DBNull) continue;
            var value = row.Cells[index];
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static string? CellOf(ResultRow row, IReadOnlyDictionary<string, int> indexes, string column) =>
        indexes.TryGetValue(column, out var index) && index < row.Cells.Count ? row.Cells[index] : null;

    /// <summary>Column name to its position; when a name repeats, the first position wins.</summary>
    private static Dictionary<string, int> FirstIndexes(IReadOnlyList<string> columns)
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < columns.Count; index++) indexes.TryAdd(columns[index], index);
        return indexes;
    }
}
