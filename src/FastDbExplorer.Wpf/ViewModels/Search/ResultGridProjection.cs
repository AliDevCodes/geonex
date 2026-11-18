using FastDbExplorer.Application.Map;
using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Search;

namespace FastDbExplorer.Wpf.ViewModels.Search;

/// <summary>Turns one page of query results into the rows the result grid displays. Pure: no UI, no database.</summary>
internal static class ResultGridProjection
{
    /// <summary>
    /// The display rows of <paramref name="result"/>: one grid cell per name in <paramref name="displayColumns"/>, in that
    /// order, plus every column's raw value for the map (<see cref="GridRow.MapValues"/>).
    /// </summary>
    public static IReadOnlyList<GridRow> ToRows(PageResult result, IReadOnlyList<string> displayColumns)
    {
        var resultIndexes = result.Columns.Select((name, index) => (name, index))
            .ToDictionary(pair => pair.name, pair => pair.index, StringComparer.Ordinal);
        var displayIndexes = displayColumns.Select(name => resultIndexes[name]).ToArray();

        return result.Rows.Select(row => new GridRow(
            displayIndexes.Select(index => GridCellFactory.Create(row[index])).ToArray(),
            QueryMapProjection.Values(result.Columns, row))).ToList();
    }
}
