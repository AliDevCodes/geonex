using System.Runtime.CompilerServices;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Search;

/// <summary>
/// Reads a query page by page. Small offsets use OFFSET; past <see cref="KeysetThreshold"/> rows a keyset query
/// continues after the last key, because a large OFFSET gets slow on big tables. One place for this rule, so the
/// grid, the row count, the export and the map read the same rows the same way.
/// </summary>
public static class PageStreamer
{
    public const long KeysetThreshold = 10_000;

    /// <summary>The key to continue after for the page at <paramref name="offset"/>, or null to use OFFSET.</summary>
    public static object?[]? AfterKeyFor(PageRequest request, long offset, object?[]? lastKey) =>
        request.UseKeyset && offset >= KeysetThreshold ? lastKey : null;

    /// <summary>Yields every row of every page in order, with the columns of its page. Cancellation is checked before each row.</summary>
    public static async IAsyncEnumerable<PageRow> ReadRowsAsync(
        Func<PageRequest, CancellationToken, Task<PageResult>> fetch,
        PageRequest request,
        int pageSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var page in ReadPagesAsync(fetch, request, pageSize, ct))
            foreach (var row in page.Rows)
            {
                ct.ThrowIfCancellationRequested();
                yield return new PageRow(page.Columns, row);
            }
    }

    /// <summary>Yields every page in order until the server reports no more rows. Cancellation is checked between pages.</summary>
    public static async IAsyncEnumerable<PageResult> ReadPagesAsync(
        Func<PageRequest, CancellationToken, Task<PageResult>> fetch,
        PageRequest request,
        int pageSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        long offset = 0;
        object?[]? afterKey = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await fetch(request with { PageSize = pageSize, Offset = offset, AfterKey = afterKey }, ct);
            yield return page;
            if (!page.HasMore) yield break;
            offset += pageSize;
            afterKey = AfterKeyFor(request, offset, page.LastKey);
        }
    }
}
