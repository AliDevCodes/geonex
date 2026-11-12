using FastDbExplorer.Application.Search;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Tests;

public sealed class PageStreamerTests
{
    private static PageRequest Request(bool keyset) =>
        new("db", "dbo", "t", [], [], FilterLogic.And, [], keyset, PageSize: 0, Offset: 0, AfterKey: null);

    /// <summary>A fake table of rows 0..total-1 (the key is the row number). Records every request it receives.</summary>
    private static (Func<PageRequest, CancellationToken, Task<PageResult>> Fetch, List<PageRequest> Seen) FakeTable(int total)
    {
        var seen = new List<PageRequest>();
        Task<PageResult> Fetch(PageRequest request, CancellationToken ct)
        {
            seen.Add(request);
            var start = request.AfterKey is { } key ? Convert.ToInt32(key[0]) + 1 : (int)request.Offset;
            var end = Math.Min(total, start + request.PageSize);
            var rows = Enumerable.Range(start, Math.Max(0, end - start)).Select(i => new object?[] { i }).ToList();
            var lastKey = rows.Count > 0 ? rows[^1] : null;
            return Task.FromResult(new PageResult(new[] { "id" }, rows, HasMore: end < total, LastKey: lastKey));
        }
        return (Fetch, seen);
    }

    private static async Task<List<int>> CollectAsync(
        Func<PageRequest, CancellationToken, Task<PageResult>> fetch,
        PageRequest request, int pageSize, CancellationToken ct = default)
    {
        var ids = new List<int>();
        await foreach (var page in PageStreamer.ReadPagesAsync(fetch, request, pageSize, ct))
            foreach (var row in page.Rows)
                ids.Add(Convert.ToInt32(row[0]));
        return ids;
    }

    [Fact]
    public async Task Every_row_is_read_across_pages()
    {
        var (fetch, _) = FakeTable(7);

        var ids = await CollectAsync(fetch, Request(keyset: false), pageSize: 3);

        Assert.Equal(Enumerable.Range(0, 7), ids);
    }

    [Fact]
    public async Task An_empty_table_gives_no_rows()
    {
        var (fetch, seen) = FakeTable(0);

        var ids = await CollectAsync(fetch, Request(keyset: false), pageSize: 3);

        Assert.Empty(ids);
        Assert.Single(seen);
    }

    [Fact]
    public async Task Past_the_threshold_keyset_paging_continues_after_the_last_key()
    {
        var total = (int)PageStreamer.KeysetThreshold + 10;
        var (fetch, seen) = FakeTable(total);

        var ids = await CollectAsync(fetch, Request(keyset: true), pageSize: 5000);

        Assert.Equal(total, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Null(seen[0].AfterKey);
        Assert.Null(seen[1].AfterKey);
        Assert.NotNull(seen[2].AfterKey);
    }

    [Fact]
    public async Task Offset_paging_never_sends_a_key()
    {
        var total = (int)PageStreamer.KeysetThreshold + 10;
        var (fetch, seen) = FakeTable(total);

        await CollectAsync(fetch, Request(keyset: false), pageSize: 5000);

        Assert.All(seen, request => Assert.Null(request.AfterKey));
    }

    [Fact]
    public async Task A_cancelled_read_stops_before_the_first_page()
    {
        var (fetch, seen) = FakeTable(10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CollectAsync(fetch, Request(keyset: false), pageSize: 3, cts.Token));
        Assert.Empty(seen);
    }
}
