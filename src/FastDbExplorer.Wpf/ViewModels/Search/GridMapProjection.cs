using FastDbExplorer.Application.Map;
using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Map;

namespace FastDbExplorer.Wpf.ViewModels.Search;

/// <summary>Turns grid rows and streamed row values into map points. Names and details are the grid's own cell text.</summary>
internal static class GridMapProjection
{
    /// <summary>A point for one row, or null when its coordinates are missing or out of range.</summary>
    public static QueryMapPoint? Project(
        IReadOnlyDictionary<string, object?> values, string latitudeColumn, string longitudeColumn,
        string? name, int rowNumber, IReadOnlyList<string> detailColumns) =>
        QueryMapProjection.TryCreatePoint(values, latitudeColumn, longitudeColumn, name, rowNumber, detailColumns,
            value => GridCellFactory.Create(value).FullValue, out var point)
            ? point
            : null;

    /// <summary>The readable text of a row's name column, or null when there is no name column.</summary>
    public static string? NameOf(IReadOnlyDictionary<string, object?> values, string? nameColumn) =>
        nameColumn is not null && values.TryGetValue(nameColumn, out var value)
            ? GridCellFactory.Create(value).Display
            : null;

    /// <summary>The points of the rows that have valid coordinates. A row is named by its first grid cell.</summary>
    public static IReadOnlyList<QueryMapPoint> ProjectRows(
        IReadOnlyList<GridRow> rows, string latitudeColumn, string longitudeColumn, IReadOnlyList<string> detailColumns)
    {
        var points = new List<QueryMapPoint>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var point = Project(row.MapValues, latitudeColumn, longitudeColumn,
                row.Cells.FirstOrDefault()?.Display, index + 1, detailColumns);
            if (point is not null) points.Add(point);
        }
        return points;
    }
}
