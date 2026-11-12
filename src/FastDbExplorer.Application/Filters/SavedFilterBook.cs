using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Filters;

/// <summary>
/// The rules of saved filters that need no UI: which filters belong to a table, and how a save is recorded.
/// </summary>
public static class SavedFilterBook
{
    /// <summary>True when the filter was saved for this exact server, database, schema and table (names ignore case).</summary>
    public static bool BelongsTo(SavedFilter filter, SavedFilterScope scope) =>
        string.Equals(filter.Server, scope.Server, StringComparison.OrdinalIgnoreCase)
        && string.Equals(filter.Database, scope.Database, StringComparison.OrdinalIgnoreCase)
        && string.Equals(filter.Schema, scope.Schema, StringComparison.OrdinalIgnoreCase)
        && string.Equals(filter.Table, scope.Table, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The filter to store under <paramref name="name"/>. A filter with the same name in the same table keeps its id,
    /// so the store overwrites it instead of adding a second one with the same name.
    /// </summary>
    public static SavedFilter Prepare(string name, SavedFilterScope scope, IEnumerable<SavedFilter> existing,
        bool useOr, IReadOnlyList<FilterCondition> conditions, DateTime utcNow)
    {
        var sameName = existing.FirstOrDefault(filter => BelongsTo(filter, scope)
            && string.Equals(filter.Name, name, StringComparison.CurrentCultureIgnoreCase));
        return new SavedFilter(sameName?.Id ?? Guid.NewGuid(), name, scope.Server, scope.Database,
            scope.Schema, scope.Table, useOr, conditions, utcNow);
    }
}
