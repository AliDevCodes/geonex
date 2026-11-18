using FastDbExplorer.Application.Connections;
using FastDbExplorer.Application.Search;
using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Connections;
using FastDbExplorer.Domain.Search;

namespace FastDbExplorer.Tests;

public class TableQuerySessionTests
{
    private static readonly ConnectionSettings Settings = new("srv", AuthenticationMode.Windows, null, null, true);

    private static PageRequest Request(params string[] columns) =>
        new("db", "dbo", "t", columns, [], FilterLogic.And, ["id"], false, PageSize: 2, Offset: 0, AfterKey: null);

    /// <summary>A table of <c>total</c> rows (id, name) that records every request it receives.</summary>
    private sealed class FakeMetadata(int total) : IDatabaseMetadataService
    {
        public List<PageRequest> Requests { get; } = [];

        public Task<PageResult> GetPageAsync(ConnectionSettings settings, PageRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var start = (int)request.Offset;
            var end = Math.Min(total, start + request.PageSize);
            var rows = Enumerable.Range(start, Math.Max(0, end - start)).Select(i => new object?[] { i, $"n{i}" }).ToList();
            return Task.FromResult(new PageResult(["id", "name"], rows, end < total, rows.Count > 0 ? rows[^1] : null));
        }

        public Task TestConnectionAsync(ConnectionSettings s, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(ConnectionSettings s, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<TableInfo>> GetTablesAsync(ConnectionSettings s, string d, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(ConnectionSettings s, string d, string sc, string t, CancellationToken ct) => throw new NotSupportedException();
    }

    private static async Task<(TableQuerySession Session, FakeMetadata Fake)> Started(int total = 5)
    {
        var fake = new FakeMetadata(total);
        var session = new TableQuerySession(fake, Settings);
        var request = Request("id", "name");
        var first = await session.FetchAsync(request, TablePager.First, CancellationToken.None);
        session.StartQuery(request, ["id", "name"], first);
        return (session, fake);
    }

    [Fact]
    public void A_new_session_has_no_query()
    {
        var session = new TableQuerySession(new FakeMetadata(1), Settings);

        Assert.False(session.HasQuery);
        Assert.Throws<InvalidOperationException>(() => session.NextCursor());
    }

    [Fact]
    public async Task Starting_a_query_makes_it_the_current_one_at_the_first_page()
    {
        var (session, _) = await Started();

        Assert.True(session.HasQuery);
        Assert.False(session.HasPrevious);
        Assert.Equal(0, session.CurrentCursor.Offset);
    }

    [Fact]
    public async Task Next_and_previous_move_the_cursor_one_page()
    {
        var (session, _) = await Started();

        var next = session.NextCursor();
        Assert.Equal(2, next.Offset);
        session.ShowPage(await session.FetchAsync(session.Request!, next, CancellationToken.None));
        session.MoveNext(next);
        Assert.True(session.HasPrevious);
        Assert.Equal(2, session.CurrentCursor.Offset);

        Assert.Equal(0, session.PreviousCursor().Offset);
        session.MovePrevious();
        Assert.False(session.HasPrevious);
    }

    [Fact]
    public async Task Fetching_reads_the_page_at_the_cursor()
    {
        var (session, fake) = await Started();

        await session.FetchAsync(session.Request!, new PageCursor(4, null), CancellationToken.None);

        Assert.Equal(4, fake.Requests[^1].Offset);
    }

    [Fact]
    public void The_computed_distance_column_is_never_asked_from_the_table()
    {
        var fake = new FakeMetadata(2);
        var session = new TableQuerySession(fake, Settings);
        session.StartQuery(Request("id"), ["id", SpatialSearch.DistanceAlias], new PageResult(["id"], [], false, null));

        Assert.Equal(new[] { "id" }, session.QueryColumns);
    }

    [Fact]
    public async Task Widening_returns_null_when_the_request_already_reads_those_columns()
    {
        var (session, _) = await Started();

        Assert.Null(session.RequestWithColumns(["name"]));
    }

    [Fact]
    public async Task Widening_adds_the_missing_columns_once()
    {
        var (session, _) = await Started();

        var widened = session.RequestWithColumns(["lat", "name", "lat"]);

        Assert.Equal(new[] { "id", "name", "lat" }, widened!.Columns);
    }

    [Fact]
    public async Task Export_streams_every_row_page_by_page()
    {
        var (session, fake) = await Started(total: 5);
        fake.Requests.Clear();

        var rows = new List<object?[]>();
        await foreach (var row in session.ExportRowsAsync(CancellationToken.None)) rows.Add(row);

        Assert.Equal(5, rows.Count);
        Assert.Equal(0, rows[0][0]);
        Assert.All(fake.Requests, request => Assert.Equal(TableQuerySession.StreamPageSize, request.PageSize));
    }

    [Fact]
    public async Task Map_values_are_keyed_by_column_name()
    {
        var (session, _) = await Started(total: 3);

        var values = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in session.MapValuesAsync(session.Request!, CancellationToken.None)) values.Add(row);

        Assert.Equal(3, values.Count);
        Assert.Equal("n1", values[1]["name"]);
    }

    [Fact]
    public async Task Counting_reports_the_real_total_when_complete()
    {
        var (session, _) = await Started(total: 5);

        RowCountProgress? last = null;
        await foreach (var step in session.CountAsync(CancellationToken.None)) last = step;

        Assert.Equal(new RowCountProgress(5, true), last);
    }
}
