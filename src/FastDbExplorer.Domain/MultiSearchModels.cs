namespace FastDbExplorer.Domain;

/// <summary>Broad type family of a column, enough to decide whether two columns can be compared directly.</summary>
public enum TypeFamily
{
    Text,
    Numeric,
    Date,
    Other
}

/// <summary>How well a search value fits two columns: fine, fine with a conversion warning, or not comparable.</summary>
public enum MatchQuality
{
    Compatible,
    NeedsConversion,
    Incompatible
}

/// <summary>
/// One database in a multi-database search: the profile it connects through, and the table and column that hold the
/// search value there. The profile is referenced by id only; connection details and passwords stay in the vault.
/// </summary>
public sealed record SearchTarget(Guid ProfileId, string Database, string Table, string Column, string TypeName);

/// <summary>
/// A named search that can be run again. It stores the filter, never the results. Relative ranges (for example the
/// last 7 days) are re-evaluated on every run; fixed ranges keep their dates.
/// </summary>
public sealed record SavedSearch(
    Guid Id,
    string Name,
    IReadOnlyList<SearchTarget> Targets,
    string SearchValue,
    int? RelativeDays,
    DateTime? FromLocal,
    DateTime? ToLocal,
    DateTime UpdatedUtc)
{
    public bool UsesRelativeRange => RelativeDays is > 0;
}
