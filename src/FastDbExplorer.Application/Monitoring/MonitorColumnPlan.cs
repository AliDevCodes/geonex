using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Monitoring;

/// <summary>
/// Which columns one check reads, and where the coordinates, the key and the map-card columns sit in each row.
/// The order of the additions matters: coordinates first, then the key, then the map-card columns. The report keeps only
/// <see cref="ReportBase"/> (the table's own columns), so map-only columns never reach the file.
/// </summary>
public sealed class MonitorColumnPlan
{
    /// <summary>Every column the query reads, in order. Only supported types, always by explicit name: never SELECT *.</summary>
    public required IReadOnlyList<string> Selected { get; init; }

    /// <summary>The table's own columns, the ones the report and the result table keep.</summary>
    public required IReadOnlyList<string> ReportBase { get; init; }

    public required int LatitudeIndex { get; init; }
    public required int LongitudeIndex { get; init; }

    /// <summary>Positions of the primary key columns in <see cref="Selected"/> (every column when the table has no key).</summary>
    public required int[] KeyIndexes { get; init; }

    /// <summary>Positions of the map-card columns in <see cref="Selected"/>.</summary>
    public required int[] DetailIndexes { get; init; }

    /// <summary>True when coordinates must be read: a proximity layer is watched or the live map is on.</summary>
    public required bool NeedCoordinates { get; init; }

    /// <summary>True when the live map is on for this monitor.</summary>
    public required bool LiveMap { get; init; }

    /// <summary>True when the live map also shows matching records that the radius excludes.</summary>
    public required bool LiveAll { get; init; }

    /// <summary>Number of the table's own columns; a report row keeps only these (plus the proximity columns).</summary>
    public int ReportWidth => ReportBase.Count;

    public static MonitorColumnPlan Build(MonitorDefinition monitor, IReadOnlyList<ColumnInfo> columns, IReadOnlyList<MapLayer> layers)
    {
        var liveMap = monitor.LiveMapEnabled && monitor.HasCoordinateColumns;
        // Display-only: with a radius, the map may also show records that conditions match but the radius excludes.
        var liveAll = liveMap && monitor.LiveMapShowAllMatches && layers.Count > 0;
        var needCoordinates = layers.Count > 0 || liveMap;

        var selected = columns.Where(c => c.IsFilterable).Select(c => c.Name).ToList();

        var latitudeIndex = -1;
        var longitudeIndex = -1;
        if (needCoordinates)
        {
            latitudeIndex = AddCoordinateColumn(monitor.LatitudeColumn!, columns, selected);
            longitudeIndex = AddCoordinateColumn(monitor.LongitudeColumn!, columns, selected);
        }
        if (selected.Count == 0) throw new ArgumentException("The table has no readable columns.");

        // Identity of a record on the map: its primary key values; without a usable key, every selected value.
        int[] keyIndexes = liveMap ? FindKeyIndexes(columns, selected) : [];

        // The report keeps exactly the table's columns. The map card's columns are read in addition, after them.
        var reportBase = selected.ToList();
        int[] detailIndexes = liveMap ? AddDetailColumns(monitor.LiveMapDetailColumns, columns, selected) : [];

        return new MonitorColumnPlan
        {
            Selected = selected,
            ReportBase = reportBase,
            LatitudeIndex = latitudeIndex,
            LongitudeIndex = longitudeIndex,
            KeyIndexes = keyIndexes,
            DetailIndexes = detailIndexes,
            NeedCoordinates = needCoordinates,
            LiveMap = liveMap,
            LiveAll = liveAll,
        };
    }

    /// <summary>A coordinate column that does not exist (or has a non-numeric type) is a configuration error and fails the check.</summary>
    private static int AddCoordinateColumn(string name, IReadOnlyList<ColumnInfo> columns, List<string> selected)
    {
        var column = columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (column is null || !MonitorCoordinateTypes.IsSupported(column.TypeName))
            throw new InvalidOperationException($"Monitor coordinate column '{name}' is missing or cannot contain numeric coordinate values.");

        var index = selected.FindIndex(c => string.Equals(c, column.Name, StringComparison.Ordinal));
        if (index < 0)
        {
            selected.Add(column.Name);
            index = selected.Count - 1;
        }
        return index;
    }

    private static int[] FindKeyIndexes(IReadOnlyList<ColumnInfo> columns, List<string> selected)
    {
        var keyNames = columns.Where(c => c.IsPrimaryKey).OrderBy(c => c.KeyOrdinal).Select(c => c.Name).ToList();
        var indexes = keyNames
            .Select(name => selected.IndexOf(name))
            .Where(index => index >= 0)
            .ToArray();
        return indexes.Length > 0 ? indexes : Enumerable.Range(0, selected.Count).ToArray();
    }

    /// <summary>
    /// Appends the columns the user chose for the map card and returns their positions. Unknown columns (removed from the
    /// table since the choice) are skipped. A monitor without a choice shows the first columns, as before.
    /// </summary>
    private static int[] AddDetailColumns(IReadOnlyList<string>? chosen, IReadOnlyList<ColumnInfo> columns, List<string> selected)
    {
        if (chosen is null)
            return Enumerable.Range(0, Math.Min(selected.Count, MonitorDefinition.DefaultLiveMapDetailColumns)).ToArray();

        var indexes = new List<int>();
        foreach (var name in chosen.Take(MonitorDefinition.MaxLiveMapDetailColumns))
        {
            var column = columns.FirstOrDefault(c => c.IsFilterable && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (column is null) continue;

            var index = selected.FindIndex(c => string.Equals(c, column.Name, StringComparison.Ordinal));
            if (index < 0)
            {
                selected.Add(column.Name);
                index = selected.Count - 1;
            }
            if (!indexes.Contains(index)) indexes.Add(index);
        }
        return [.. indexes];
    }
}
