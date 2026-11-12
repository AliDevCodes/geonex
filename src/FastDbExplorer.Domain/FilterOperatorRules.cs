namespace FastDbExplorer.Domain;

/// <summary>Which values each filter operator needs. Used by the explorer rows and by anything that validates a condition.</summary>
public static class FilterOperatorRules
{
    public static bool NeedsValue(FilterOperator op) => op is not (FilterOperator.IsNull or FilterOperator.IsNotNull);

    public static bool NeedsSecondValue(FilterOperator op) => op == FilterOperator.Between;

    /// <summary>A column is chosen, and every value the operator needs is filled in.</summary>
    public static bool IsComplete(string? column, FilterOperator op, string? value, string? value2) =>
        column is not null
        && (!NeedsValue(op) || !string.IsNullOrWhiteSpace(value))
        && (!NeedsSecondValue(op) || !string.IsNullOrWhiteSpace(value2));
}
