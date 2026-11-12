using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Search;

/// <summary>A nearby-search centre and radius, as the user chose them. Units: degrees and metres.</summary>
public sealed record NearbyPoint(double Latitude, double Longitude, double RadiusMeters);

/// <summary>Why a table query cannot be built. <see cref="None"/> means the request is ready.</summary>
public enum QueryPlanError { None, NoColumns, IncompleteFilter, NoCoordinateColumns, NoOrder }

/// <summary>Everything the table screen knows, as plain values. The planner reads nothing else.</summary>
public sealed record QueryPlanInput(
    string Database,
    string Schema,
    string Table,
    IReadOnlyList<ColumnInfo> Meta,
    IReadOnlyList<string> SelectedColumns,
    bool FiltersComplete,
    IReadOnlyList<FilterCondition> Filters,
    bool UseOr,
    bool UsesPrimaryKey,
    IReadOnlyList<string> KeyColumns,
    string? OrderColumn,
    IReadOnlyList<string> MapRequiredColumns,
    string? LatitudeColumn,
    string? LongitudeColumn,
    NearbyPoint? Nearby,
    int PageSize);

public sealed record QueryPlanResult(QueryPlanError Error, PageRequest? Request, IReadOnlyList<string> DisplayColumns);

/// <summary>
/// Turns the table screen's choices into one page request. Checks run in a fixed order (columns, filters, nearby
/// coordinates, order) so the first problem reported is always the same one. Pure: no UI, no database.
/// </summary>
public static class QueryRequestPlanner
{
    public static QueryPlanResult Plan(QueryPlanInput input)
    {
        if (input.SelectedColumns.Count == 0) return Fail(QueryPlanError.NoColumns);
        if (!input.FiltersComplete) return Fail(QueryPlanError.IncompleteFilter);

        SpatialCondition? spatial = null;
        if (input.Nearby is { } point)
        {
            var target = ResolveSpatialTarget(input);
            if (target is null) return Fail(QueryPlanError.NoCoordinateColumns);
            spatial = new SpatialCondition(target.GeographyColumn, target.LatColumn, target.LonColumn,
                point.Latitude, point.Longitude, point.RadiusMeters);
        }

        // With a nearby point the rows are ordered by distance, so the primary key or the chosen order is optional.
        IReadOnlyList<string> order;
        if (input.UsesPrimaryKey) order = input.KeyColumns;
        else if (input.OrderColumn is not null) order = [input.OrderColumn];
        else if (spatial is not null) order = [];
        else return Fail(QueryPlanError.NoOrder);

        var selected = input.SelectedColumns;
        IReadOnlyList<string> displayColumns = spatial is null
            ? selected
            : selected.Append(SpatialSearch.DistanceAlias).ToList();
        // The distance is a computed output, so it never reaches the SQL column list.
        var queryColumns = selected.Concat(input.MapRequiredColumns).Distinct(StringComparer.Ordinal).ToList();

        var request = new PageRequest(input.Database, input.Schema, input.Table, queryColumns, input.Filters,
            input.UseOr ? FilterLogic.Or : FilterLogic.And, order, input.UsesPrimaryKey && spatial is null,
            input.PageSize, 0, null, null, spatial);
        return new QueryPlanResult(QueryPlanError.None, request, displayColumns);
    }

    private static QueryPlanResult Fail(QueryPlanError error) => new(error, null, []);

    private static SpatialTarget? ResolveSpatialTarget(QueryPlanInput input)
    {
        // Columns the user picked for the map win; otherwise detect them from the table.
        var latitude = input.LatitudeColumn;
        var longitude = input.LongitudeColumn;
        if (latitude is not null && longitude is not null && latitude != longitude
            && input.Meta.Any(c => c.Name == latitude) && input.Meta.Any(c => c.Name == longitude))
            return new SpatialTarget(SpatialMode.LatLonColumns, latitude, longitude, null);
        return SpatialSearch.Detect(input.Meta);
    }
}
