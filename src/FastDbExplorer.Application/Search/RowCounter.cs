using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Search;

namespace FastDbExplorer.Application.Search;

/// <summary>The running total of a count after one more page. <see cref="Complete"/> is true on the last page.</summary>
public sealed record RowCountProgress(long Total, bool Complete);

/// <summary>
/// Counts the rows of a search page by page, without COUNT(*), and reports the running total after each page.
/// Pure: no UI, no database.
/// </summary>
public static class RowCounter
{
    public static async IAsyncEnumerable<RowCountProgress> CountAsync(
        Func<PageRequest, CancellationToken, Task<PageResult>> fetch,
        PageRequest request,
        int pageSize,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        long total = 0;
        await foreach (var page in PageStreamer.ReadPagesAsync(fetch, request, pageSize, ct))
        {
            total += page.Rows.Count;
            yield return new RowCountProgress(total, Complete: !page.HasMore);
        }
    }
}
