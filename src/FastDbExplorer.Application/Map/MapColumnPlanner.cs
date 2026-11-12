namespace FastDbExplorer.Application.Map;

/// <summary>Decides which columns a map-related query must read. Pure: no UI, no database.</summary>
public static class MapColumnPlanner
{
    /// <summary>
    /// Columns a grid query must also read so the map can use its rows later: the coordinates (only when they are real
    /// coordinate columns), the chosen detail columns, and the sort key that keeps the rows in the same order.
    /// </summary>
    public static IReadOnlyList<string> RequiredForGrid(
        IReadOnlyList<string> coordinateColumns,
        string? latitudeColumn,
        string? longitudeColumn,
        IReadOnlyList<string> selectedDetailColumns,
        bool usesPrimaryKey,
        IReadOnlyList<string> keyColumns,
        string? orderColumn)
    {
        var orderColumns = usesPrimaryKey ? keyColumns.ToList() : new List<string>();
        if (!usesPrimaryKey && orderColumn is not null) orderColumns.Add(orderColumn);

        return new[] { latitudeColumn, longitudeColumn }
            .Where(name => name is not null && coordinateColumns.Contains(name))
            .Cast<string>()
            .Concat(selectedDetailColumns)
            .Concat(orderColumns)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Columns read when every row of a search goes to the map: the two coordinates, the name column and at most
    /// <paramref name="maxDetailColumns"/> detail columns, without duplicates.
    /// </summary>
    public static IReadOnlyList<string> ForAllRows(
        string latitudeColumn,
        string longitudeColumn,
        string? nameColumn,
        IReadOnlyList<string> selectedDetailColumns,
        int maxDetailColumns)
    {
        var columns = new List<string> { latitudeColumn, longitudeColumn };
        if (nameColumn is not null) columns.Add(nameColumn);
        columns.AddRange(selectedDetailColumns.Take(maxDetailColumns));
        return columns.Distinct(StringComparer.Ordinal).ToList();
    }
}
