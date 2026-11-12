namespace FastDbExplorer.Domain;

/// <summary>Where a saved filter belongs: one server, database and table.</summary>
public sealed record SavedFilterScope(string Server, string Database, string Schema, string Table);
