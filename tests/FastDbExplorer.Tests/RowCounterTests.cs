using FastDbExplorer.Application.Search;
using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Search;

namespace FastDbExplorer.Tests;

public class RowCounterTests
{
    private static PageRequest Request() =>
        new("db", "dbo", "t", [], [], FilterLogic.And, [], false, PageSize: 0, Offset: 0, AfterKey: null);

    /// <summary>A fake table of <paramref name="total"/> rows with one column "id".</summary>
    private static Func<PageRequest, CancellationToken, Task<PageResult>> Table(int total) =>
        (request, ct) =>
        {
            var start = (int)request.Offset;
            var end = Math.Min(total, start + request.PageSize);
            var rows = Enumerable.Range(start, Math.Max(0, end - start)).Select(i => new object?[] { i }).ToList();
            return Task.FromResult(new PageResult(["id"], rows, HasMore: end < total, LastKey: rows.Count > 0 ? rows[^1] : null));
        };

    [Fact]
    public async Task The_running_total_grows_page_by_page_and_completes_on_the_last_page()
    {
        var progress = new List<RowCountProgress>();
        await foreach (var step in RowCounter.CountAsync(Table(5), Request(), pageSize: 2, CancellationToken.None))
            progress.Add(step);

        Assert.Equal(new[] { new RowCountProgress(2, false), new RowCountProgress(4, false), new RowCountProgress(5, true) }, progress);
    }

    [Fact]
    public async Task An_empty_table_reports_zero_as_complete_once()
    {
        var progress = new List<RowCountProgress>();
        await foreach (var step in RowCounter.CountAsync(Table(0), Request(), pageSize: 2, CancellationToken.None))
            progress.Add(step);

        Assert.Equal(new[] { new RowCountProgress(0, true) }, progress);
    }

    [Fact]
    public async Task A_cancelled_count_stops_with_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var step in RowCounter.CountAsync(Table(5), Request(), pageSize: 2, cts.Token))
                _ = step;
        });
    }
}
