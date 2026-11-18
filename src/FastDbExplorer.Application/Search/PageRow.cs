namespace FastDbExplorer.Application.Search;

/// <summary>One row of a streamed query, with the column names of the page it came from.</summary>
public sealed record PageRow(IReadOnlyList<string> Columns, object?[] Values);
