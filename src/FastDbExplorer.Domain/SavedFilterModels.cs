namespace FastDbExplorer.Domain;

/// <summary>
/// A named set of Explorer filter conditions for one table. Stores the table identity (server, database, schema, table)
/// so the filter is offered only where it applies. Never stores passwords or connection secrets.
/// </summary>
public sealed record SavedFilter(
    Guid Id,
    string Name,
    string Server,
    string Database,
    string Schema,
    string Table,
    bool UseOr,
    IReadOnlyList<FilterCondition> Conditions,
    DateTime UpdatedUtc);
