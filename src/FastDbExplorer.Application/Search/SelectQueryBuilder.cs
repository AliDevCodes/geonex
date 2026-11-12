using System.Data;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Search;

public sealed record QueryParameter(string Name, SqlDbType Type, object Value);

public sealed record BuiltQuery(string Sql, IReadOnlyList<QueryParameter> Parameters);

/// <summary>
/// Builds a parameterized SELECT. Identifiers come from the real column list and are bracket-quoted;
/// values are never concatenated into SQL; no SELECT *; paging always has a deterministic ORDER BY.
/// It asks for pageSize + 1 rows so the caller can tell whether a next page exists without COUNT(*).
/// </summary>
public static class SelectQueryBuilder
{
    public const int MaxPageSize = 1000;
    public const int MaxFilters = 20;
    public const int MaxInValues = 100;

    public static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    public static BuiltQuery Build(
        PageRequest request, IReadOnlyList<ColumnInfo> tableColumns, bool supportsOffsetFetch = true)
    {
        var columns = new ColumnLookup(tableColumns);
        RequireValid(request);

        if (request.Spatial is not null)
        {
            // Nearby search pages by offset: ordering is by distance, which keyset paging cannot follow.
            if (request.AfterKey is not null) throw new ArgumentException("Nearby search does not support key paging.");
            SpatialSearch.Validate(request.Spatial.Latitude, request.Spatial.Longitude, request.Spatial.RadiusMeters);
            request = request with { UseKeyset = false };
        }

        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var parameters = new ParameterList();
        var visibleColumns = request.Columns.Select(c => Quote(columns.Get(c).Name)).ToList();
        var usedAliases = new HashSet<string>(request.Columns.Select(c => columns.Get(c).Name), StringComparer.OrdinalIgnoreCase);
        var select = visibleColumns.ToList();
        var keyAliases = request.UseKeyset
            ? AddKeyAliases(request, columns, usedAliases, select)
            : new List<string>();

        string? distanceSql = null;
        string? spatialWhere = null;
        if (request.Spatial is { } spatial)
        {
            var target = spatial.GeographyColumn is not null
                ? new SpatialTarget(SpatialMode.GeographyColumn, null, null, spatial.GeographyColumn)
                : new SpatialTarget(SpatialMode.LatLonColumns, spatial.LatitudeColumn, spatial.LongitudeColumn, null);
            var latParam = parameters.Add(SqlDbType.Float, spatial.Latitude);
            var lonParam = parameters.Add(SqlDbType.Float, spatial.Longitude);
            var radiusParam = parameters.Add(SqlDbType.Float, spatial.RadiusMeters);
            var (distance, present) = SpatialSearch.Expressions(target, latParam, lonParam, name => Quote(columns.Get(name).Name));
            distanceSql = distance;
            select.Add($"{distance} AS {Quote(SpatialSearch.DistanceAlias)}");
            spatialWhere = $"{present} AND {distance} <= {radiusParam}";
        }

        var where = BuildWhere(request, columns, parameters, spatialWhere);

        var orderTerms = request.OrderBy.Select(c => Quote(columns.Get(c).Name) + " ASC").ToList();
        if (distanceSql is not null) orderTerms.Insert(0, distanceSql + " ASC");

        var clauses = new Clauses(
            Select: string.Join(", ", select),
            From: $"{Quote(request.Schema)}.{Quote(request.Table)}",
            Where: where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "",
            OrderBy: string.Join(", ", orderTerms));

        var sql = supportsOffsetFetch
            ? OffsetSql(request, pageSize, clauses, parameters)
            : RowNumberSql(request, pageSize, clauses, OutputColumns(visibleColumns, distanceSql, keyAliases), usedAliases, parameters);

        return new BuiltQuery(sql, parameters.Items);
    }

    /// <summary>Shape and size limits, checked before any SQL is written.</summary>
    private static void RequireValid(PageRequest request)
    {
        if (request.Columns.Count == 0) throw new ArgumentException("Select at least one column.");
        if (request.OrderBy.Count == 0 && request.Spatial is null)
            throw new ArgumentException("An ORDER BY column is required for paging.");
        if (request.Filters.Count + (request.RequiredFilters?.Count ?? 0) > MaxFilters)
            throw new ArgumentException($"At most {MaxFilters} filters are allowed.");
        if (request.AfterKey is not null && (!request.UseKeyset || request.AfterKey.Length != request.OrderBy.Count))
            throw new ArgumentException("Invalid keyset position.");
    }

    /// <summary>Adds one alias per order column so the caller can read the last key of a page.</summary>
    private static List<string> AddKeyAliases(PageRequest request, ColumnLookup columns, HashSet<string> usedAliases, List<string> select)
    {
        var aliases = new List<string>();
        for (var i = 0; i < request.OrderBy.Count; i++)
        {
            var alias = Quote(UniqueAlias($"__k{i}", usedAliases));
            aliases.Add(alias);
            select.Add($"{Quote(columns.Get(request.OrderBy[i]).Name)} AS {alias}");
        }
        return aliases;
    }

    /// <summary>The columns the caller receives: the visible ones, then the distance, then the keys.</summary>
    private static List<string> OutputColumns(List<string> visibleColumns, string? distanceSql, List<string> keyAliases)
    {
        var output = new List<string>(visibleColumns);
        if (distanceSql is not null) output.Add(Quote(SpatialSearch.DistanceAlias));
        output.AddRange(keyAliases);
        return output;
    }

    /// <summary>Filters, required filters, the keyset position and the spatial radius, joined by AND.</summary>
    private static List<string> BuildWhere(PageRequest request, ColumnLookup columns, ParameterList parameters, string? spatialWhere)
    {
        var where = new List<string>();
        var filters = request.Filters.Select(f => BuildFilter(f, columns.Get(f.Column), parameters.Add)).ToList();
        if (filters.Count > 0)
            where.Add("(" + string.Join(request.Logic == FilterLogic.And ? " AND " : " OR ", filters) + ")");
        if (request.RequiredFilters is { Count: > 0 })
            where.AddRange(request.RequiredFilters.Select(f => BuildFilter(f, columns.Get(f.Column), parameters.Add)).ToList());
        if (request.AfterKey is not null)
            where.Add(BuildKeyset(request, columns.Get, parameters.Add));
        if (spatialWhere is not null)
            where.Add(spatialWhere);
        return where;
    }

    /// <summary>OFFSET/FETCH paging. A keyset page starts at the top, because its WHERE clause already skips the rows before it.</summary>
    private static string OffsetSql(PageRequest request, int pageSize, Clauses clauses, ParameterList parameters)
    {
        var offset = request.AfterKey is null ? "@offset" : "0";
        var sql = $"SELECT {clauses.Select} FROM {clauses.From}{clauses.Where} ORDER BY {clauses.OrderBy} OFFSET {offset} ROWS FETCH NEXT @take ROWS ONLY";
        if (request.AfterKey is null)
            parameters.AddNamed("@offset", SqlDbType.BigInt, Math.Max(0, request.Offset));
        parameters.AddNamed("@take", SqlDbType.Int, pageSize + 1);
        return sql;
    }

    /// <summary>ROW_NUMBER paging for servers without OFFSET/FETCH.</summary>
    private static string RowNumberSql(PageRequest request, int pageSize, Clauses clauses,
        List<string> outputColumns, HashSet<string> usedAliases, ParameterList parameters)
    {
        var rowNumberAlias = Quote(UniqueAlias("__fdb_row_number", usedAliases));
        var inner = $"SELECT {clauses.Select}, ROW_NUMBER() OVER (ORDER BY {clauses.OrderBy}) AS {rowNumberAlias} FROM {clauses.From}{clauses.Where}";
        var offset = request.AfterKey is null ? Math.Max(0, request.Offset) : 0;
        parameters.AddNamed("@offset", SqlDbType.BigInt, offset);
        parameters.AddNamed("@take", SqlDbType.Int, pageSize + 1);
        return $"SELECT {string.Join(", ", outputColumns)} FROM ({inner}) AS [__fdb_page] WHERE {rowNumberAlias} > @offset AND {rowNumberAlias} <= @offset + @take ORDER BY {rowNumberAlias}";
    }

    private static string UniqueAlias(string candidate, HashSet<string> used)
    {
        var alias = candidate;
        var suffix = 1;
        while (!used.Add(alias)) alias = $"{candidate}_{suffix++}";
        return alias;
    }

    private static string BuildKeyset(PageRequest r, Func<string, ColumnInfo> col, Func<SqlDbType, object, string> add)
    {
        string KeyParam(int index)
        {
            var column = col(r.OrderBy[index]);
            var value = r.AfterKey![index] ?? throw new ArgumentException("Key value cannot be null.");
            return add(ValueConverter.GetSqlDbType(column.TypeName), value);
        }

        // (k0 > a0) OR (k0 = a0 AND k1 > a1) OR ...  — works for single and composite keys.
        var terms = new List<string>();
        for (var i = 0; i < r.OrderBy.Count; i++)
        {
            var parts = new List<string>();
            for (var j = 0; j < i; j++)
                parts.Add($"{Quote(col(r.OrderBy[j]).Name)} = {KeyParam(j)}");
            parts.Add($"{Quote(col(r.OrderBy[i]).Name)} > {KeyParam(i)}");
            terms.Add("(" + string.Join(" AND ", parts) + ")");
        }
        return "(" + string.Join(" OR ", terms) + ")";
    }

    private static string BuildFilter(FilterCondition f, ColumnInfo col, Func<SqlDbType, object, string> add)
    {
        var c = Quote(col.Name);
        if (f.Operator == FilterOperator.IsNull) return $"{c} IS NULL";
        if (f.Operator == FilterOperator.IsNotNull) return $"{c} IS NOT NULL";

        if (!col.IsFilterable)
            throw new NotSupportedException($"Column '{col.Name}' ({col.TypeName}) cannot be filtered.");
        if (string.IsNullOrWhiteSpace(f.Value))
            throw new ArgumentException($"A value is required for column '{col.Name}'.");

        var type = ValueConverter.GetSqlDbType(col.TypeName);
        string One(string text) => add(type, ValueConverter.Parse(text, col.TypeName));

        switch (f.Operator)
        {
            case FilterOperator.EqualTo: return $"{c} = {One(f.Value)}";
            case FilterOperator.NotEqualTo: return $"{c} <> {One(f.Value)}";
            case FilterOperator.GreaterThan: return $"{c} > {One(f.Value)}";
            case FilterOperator.GreaterOrEqual: return $"{c} >= {One(f.Value)}";
            case FilterOperator.LessThan: return $"{c} < {One(f.Value)}";
            case FilterOperator.LessOrEqual: return $"{c} <= {One(f.Value)}";

            case FilterOperator.Between:
                if (string.IsNullOrWhiteSpace(f.Value2))
                    throw new ArgumentException($"A second value is required for column '{col.Name}'.");
                return $"{c} BETWEEN {One(f.Value)} AND {One(f.Value2)}";

            case FilterOperator.In:
                var items = f.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (items.Length == 0 || items.Length > MaxInValues)
                    throw new ArgumentException($"IN needs 1 to {MaxInValues} values.");
                return $"{c} IN ({string.Join(", ", items.Select(One))})";

            case FilterOperator.Contains:
            case FilterOperator.StartsWith:
            case FilterOperator.EndsWith:
                if (!col.IsText)
                    throw new ArgumentException($"Text operators need a text column; '{col.Name}' is {col.TypeName}.");
                var escaped = EscapeLike(f.Value);
                var pattern = f.Operator switch
                {
                    FilterOperator.Contains => $"%{escaped}%",
                    FilterOperator.StartsWith => $"{escaped}%",
                    _ => $"%{escaped}"
                };
                return $"{c} LIKE {add(type, pattern)} ESCAPE '\\'";

            default:
                throw new ArgumentOutOfRangeException(nameof(f), f.Operator, "Unknown operator.");
        }
    }

    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    /// <summary>Column definitions by name. An unknown name is a caller error.</summary>
    private sealed class ColumnLookup(IReadOnlyList<ColumnInfo> tableColumns)
    {
        private readonly Dictionary<string, ColumnInfo> _byName = tableColumns.ToDictionary(c => c.Name, StringComparer.Ordinal);

        public ColumnInfo Get(string name) =>
            _byName.TryGetValue(name, out var c) ? c : throw new ArgumentException($"Unknown column '{name}'.");
    }

    /// <summary>Named parameters in the order they are added: @p0, @p1, ... Paging parameters use fixed names.</summary>
    private sealed class ParameterList
    {
        private readonly List<QueryParameter> _items = [];

        public IReadOnlyList<QueryParameter> Items => _items;

        public string Add(SqlDbType type, object value)
        {
            var name = "@p" + _items.Count;
            _items.Add(new QueryParameter(name, type, value));
            return name;
        }

        public void AddNamed(string name, SqlDbType type, object value) =>
            _items.Add(new QueryParameter(name, type, value));
    }

    /// <summary>The fixed clauses of a SELECT, already rendered.</summary>
    private sealed record Clauses(string Select, string From, string Where, string OrderBy);
}
