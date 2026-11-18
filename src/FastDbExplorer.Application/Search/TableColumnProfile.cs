using FastDbExplorer.Application.Map;
using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Search;

namespace FastDbExplorer.Application.Search;

/// <summary>What the table screen derives from a table's columns once they are loaded. Pure: no UI, no database.</summary>
public sealed record TableColumnProfile(
    IReadOnlyList<string> KeyColumnNames,
    bool UsesPrimaryKey,
    IReadOnlyList<string> FilterableColumns,
    string? LatitudeColumn,
    string? LongitudeColumn,
    HashSet<string> DefaultDetailColumns)
{
    public string KeyDescription => string.Join(", ", KeyColumnNames);

    /// <param name="columns">All columns of the table.</param>
    /// <param name="displayColumn">The first column the grid shows; it names the map points, so it is not a default detail.</param>
    /// <param name="latitudeColumn">A latitude column already chosen; kept when present, otherwise detected from the names.</param>
    /// <param name="longitudeColumn">A longitude column already chosen; kept when present, otherwise detected from the names.</param>
    public static TableColumnProfile From(
        IReadOnlyList<ColumnInfo> columns, string? displayColumn, string? latitudeColumn, string? longitudeColumn)
    {
        var keys = columns.Where(column => column.IsPrimaryKey).OrderBy(column => column.KeyOrdinal).ToList();
        var filterable = columns.Where(column => column.IsFilterable).Select(column => column.Name).ToList();
        var latitude = latitudeColumn ?? QueryMapProjection.DetectCoordinateColumn(columns, true);
        var longitude = longitudeColumn ?? QueryMapProjection.DetectCoordinateColumn(columns, false);

        // Coordinates and the display column are already on the map card, so they are never default details.
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[] { displayColumn, latitude, longitude })
            if (name is not null) excluded.Add(name);

        return new TableColumnProfile(
            keys.Select(column => column.Name).ToList(),
            keys.Count > 0 && keys.All(column => column.IsFilterable),
            filterable,
            latitude,
            longitude,
            PreviewColumnDetector.DefaultDetails(filterable, excluded));
    }
}
