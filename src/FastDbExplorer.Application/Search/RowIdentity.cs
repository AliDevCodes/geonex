namespace FastDbExplorer.Application.Search;

/// <summary>
/// Checks that a refreshed page still holds the same rows in the same order, judged only by the identity columns.
/// Pure: no UI, no database.
/// </summary>
public static class RowIdentity
{
    /// <summary>
    /// True when every row of the previous page has, in each identity column, the same value as the row at the same
    /// position of the refreshed page. A missing identity column or a different row count means not the same.
    /// </summary>
    public static bool SameRows(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> previous,
        IReadOnlyList<string> currentColumns,
        IReadOnlyList<object?[]> currentRows,
        IReadOnlyList<string> identityColumns)
    {
        if (previous.Count != currentRows.Count) return false;

        var indexes = currentColumns.Select((name, index) => (name, index))
            .ToDictionary(pair => pair.name, pair => pair.index, StringComparer.Ordinal);
        for (var rowIndex = 0; rowIndex < previous.Count; rowIndex++)
            foreach (var column in identityColumns)
                if (!previous[rowIndex].TryGetValue(column, out var before)
                    || !indexes.TryGetValue(column, out var currentIndex)
                    || !ValuesEqual(before, currentRows[rowIndex][currentIndex]))
                    return false;
        return true;
    }

    /// <summary>Equal values; byte arrays are compared by their content.</summary>
    public static bool ValuesEqual(object? left, object? right) =>
        left is byte[] leftBytes && right is byte[] rightBytes
            ? leftBytes.AsSpan().SequenceEqual(rightBytes)
            : Equals(left, right);
}
