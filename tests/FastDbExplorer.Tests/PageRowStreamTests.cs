using FastDbExplorer.Application.Search;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Tests;

public class PageRowStreamTests
{
    private const int Total = 5;

    private static PageRequest Request() =>
        new("db", "dbo", "t", [], [], FilterLogic.And, [], false, PageSize: 0, Offset: 0, AfterKey: null);

    /// <summary>A fake table of rows 0..4 with one column "id".</summary>
    private static Task<PageResult> Fetch(PageRequest request, CancellationToken ct)
    {
        var start = (int)request.Offset;
        var end = Math.Min(Total, start + request.PageSize);
        var rows = Enumerable.Range(start, Math.Max(0, end - start)).Select(i => new object?[] { i }).ToList();
        var lastKey = rows.Count > 0 ? rows[^1] : null;
        return Task.FromResult(new PageResult(["id"], rows, HasMore: end < Total, LastKey: lastKey));
    }

    [Fact]
    public async Task Rows_of_every_page_come_out_in_order_with_their_columns()
    {
        var values = new List<int>();
        var columns = new HashSet<string>();
        await foreach (var row in PageStreamer.ReadRowsAsync(Fetch, Request(), pageSize: 2, CancellationToken.None))
        {
            values.Add((int)row.Values[0]!);
            foreach (var column in row.Columns) columns.Add(column);
        }

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, values);
        Assert.Equal(new HashSet<string> { "id" }, columns);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_stream_before_any_row()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var row in PageStreamer.ReadRowsAsync(Fetch, Request(), pageSize: 2, cts.Token))
                _ = row;
        });
    }

    [Fact]
    public async Task An_empty_table_yields_no_rows()
    {
        Task<PageResult> Empty(PageRequest request, CancellationToken ct) =>
            Task.FromResult(new PageResult(["id"], [], HasMore: false, LastKey: null));

        var count = 0;
        await foreach (var row in PageStreamer.ReadRowsAsync(Empty, Request(), pageSize: 2, CancellationToken.None))
            count++;

        Assert.Equal(0, count);
    }
}
