using FastDbExplorer.Application.Search;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Tests;

public class QueryRequestPlannerTests
{
    private static readonly IReadOnlyList<ColumnInfo> TableWithCoordinates =
    [
        new("Id", "int", 1),
        new("Name", "nvarchar", 0),
        new("Latitude", "float", 0),
        new("Longitude", "float", 0),
    ];

    private static readonly IReadOnlyList<ColumnInfo> TableWithoutCoordinates =
    [
        new("Id", "int", 1),
        new("Name", "nvarchar", 0),
    ];

    private static QueryPlanInput Input(
        IReadOnlyList<string>? selected = null,
        bool filtersComplete = true,
        bool usesKey = true,
        string? orderColumn = null,
        NearbyPoint? nearby = null,
        bool useOr = false,
        IReadOnlyList<string>? mapColumns = null,
        IReadOnlyList<ColumnInfo>? meta = null) => new(
        Database: "sales", Schema: "dbo", Table: "orders", Meta: meta ?? TableWithCoordinates,
        SelectedColumns: selected ?? ["Name"], FiltersComplete: filtersComplete,
        Filters: [new FilterCondition("Name", FilterOperator.Contains, "x")], UseOr: useOr,
        UsesPrimaryKey: usesKey, KeyColumns: ["Id"], OrderColumn: orderColumn,
        MapRequiredColumns: mapColumns ?? [], LatitudeColumn: null, LongitudeColumn: null,
        Nearby: nearby, PageSize: 100);

    [Fact]
    public void Without_a_selected_column_the_plan_fails_with_NoColumns()
    {
        var result = QueryRequestPlanner.Plan(Input(selected: []));

        Assert.Equal(QueryPlanError.NoColumns, result.Error);
        Assert.Null(result.Request);
    }

    [Fact]
    public void An_incomplete_filter_fails_with_IncompleteFilter()
    {
        var result = QueryRequestPlanner.Plan(Input(filtersComplete: false));

        Assert.Equal(QueryPlanError.IncompleteFilter, result.Error);
    }

    [Fact]
    public void A_missing_column_is_reported_before_an_incomplete_filter()
    {
        var result = QueryRequestPlanner.Plan(Input(selected: [], filtersComplete: false));

        Assert.Equal(QueryPlanError.NoColumns, result.Error);
    }

    [Fact]
    public void Without_a_key_or_an_order_column_the_plan_fails_with_NoOrder()
    {
        var result = QueryRequestPlanner.Plan(Input(usesKey: false, orderColumn: null));

        Assert.Equal(QueryPlanError.NoOrder, result.Error);
    }

    [Fact]
    public void A_primary_key_order_enables_keyset_paging()
    {
        var result = QueryRequestPlanner.Plan(Input(usesKey: true));

        Assert.Equal(QueryPlanError.None, result.Error);
        Assert.Equal(new[] { "Id" }, result.Request!.OrderBy);
        Assert.True(result.Request.UseKeyset);
        Assert.Equal(100, result.Request.PageSize);
        Assert.Equal(0, result.Request.Offset);
    }

    [Fact]
    public void A_chosen_order_column_is_used_when_there_is_no_key_and_paging_is_offset_based()
    {
        var result = QueryRequestPlanner.Plan(Input(usesKey: false, orderColumn: "Name"));

        Assert.Equal(new[] { "Name" }, result.Request!.OrderBy);
        Assert.False(result.Request.UseKeyset);
    }

    [Fact]
    public void Or_logic_is_passed_through()
    {
        var result = QueryRequestPlanner.Plan(Input(useOr: true));

        Assert.Equal(FilterLogic.Or, result.Request!.Logic);
    }

    [Fact]
    public void A_nearby_search_adds_the_distance_column_for_display_only()
    {
        var result = QueryRequestPlanner.Plan(Input(usesKey: false, nearby: new NearbyPoint(35.7, 51.4, 500)));

        Assert.Equal(QueryPlanError.None, result.Error);
        Assert.NotNull(result.Request!.Spatial);
        Assert.Equal("Latitude", result.Request.Spatial!.LatitudeColumn);
        Assert.Equal("Longitude", result.Request.Spatial.LongitudeColumn);
        Assert.Equal(500, result.Request.Spatial.RadiusMeters);
        Assert.Equal(new[] { "Name", SpatialSearch.DistanceAlias }, result.DisplayColumns);
        Assert.DoesNotContain(SpatialSearch.DistanceAlias, result.Request.Columns);
    }

    [Fact]
    public void A_nearby_search_with_no_key_and_no_order_column_is_ordered_by_distance()
    {
        var result = QueryRequestPlanner.Plan(Input(usesKey: false, nearby: new NearbyPoint(35.7, 51.4, 500)));

        Assert.Empty(result.Request!.OrderBy);
        Assert.False(result.Request.UseKeyset);
    }

    [Fact]
    public void A_nearby_search_on_a_table_without_coordinates_fails_with_NoCoordinateColumns()
    {
        var result = QueryRequestPlanner.Plan(Input(
            nearby: new NearbyPoint(35.7, 51.4, 500), meta: TableWithoutCoordinates));

        Assert.Equal(QueryPlanError.NoCoordinateColumns, result.Error);
    }

    [Fact]
    public void Map_columns_are_added_once_and_keep_their_order()
    {
        var result = QueryRequestPlanner.Plan(Input(
            selected: ["Name"], usesKey: false, orderColumn: "Name", mapColumns: ["Name", "Latitude"]));

        Assert.Equal(new[] { "Name", "Latitude" }, result.Request!.Columns);
        Assert.Equal(new[] { "Name" }, result.DisplayColumns);
    }
}
