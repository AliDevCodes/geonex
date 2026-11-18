using System.Runtime.CompilerServices;
using FastDbExplorer.Application.Connections;
using FastDbExplorer.Application.Map;
using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Connections;
using FastDbExplorer.Domain.Search;

namespace FastDbExplorer.Application.Search;

/// <summary>
/// The query of one table screen and its data access: the request on screen, its display columns, the last page, where the
/// pager is, and every way rows are read (a page, the export stream, the map stream, the count). It never touches a UI:
/// the caller decides what is busy, what is shown and what an error says.
/// </summary>
public sealed class TableQuerySession(IDatabaseMetadataService metadata, ConnectionSettings settings)
{
    public const int StreamPageSize = 500;
    public const int CountPageSize = 500;

    private readonly TablePager _pager = new();

    public PageRequest? Request { get; private set; }
    public IReadOnlyList<string> DisplayColumns { get; private set; } = [];
    public PageResult? LastResult { get; private set; }

    /// <summary>True once a query has produced a first page, so Next, Previous and the streams can work.</summary>
    public bool HasQuery => Request is not null && LastResult is not null;

    public bool HasPrevious => _pager.HasPrevious;
    public PageCursor CurrentCursor => _pager.Current;

    /// <summary>The real columns of the display columns: the nearby distance is computed, so requests never ask for it.</summary>
    public IReadOnlyList<string> QueryColumns =>
        DisplayColumns.Where(column => column != SpatialSearch.DistanceAlias).ToList();

    /// <summary>Reads one page of <paramref name="request"/> at <paramref name="cursor"/>. Throws what the service throws.</summary>
    public Task<PageResult> FetchAsync(PageRequest request, PageCursor cursor, CancellationToken ct) =>
        FetchPage(request with { Offset = cursor.Offset, AfterKey = cursor.AfterKey }, ct);

    /// <summary>A new query produced its first page: it becomes the session's query and the pager starts again.</summary>
    public void StartQuery(PageRequest request, IReadOnlyList<string> displayColumns, PageResult firstPage)
    {
        Request = request;
        DisplayColumns = displayColumns;
        _pager.Reset();
        LastResult = firstPage;
    }

    /// <summary>The page now on screen.</summary>
    public void ShowPage(PageResult page) => LastResult = page;

    /// <summary>Keeps the same query and page but reads different columns.</summary>
    public void ReplaceRequest(PageRequest request) => Request = request;

    /// <summary>
    /// The request that also reads <paramref name="extraColumns"/>, or null when the current request already reads exactly the
    /// columns that would be needed.
    /// </summary>
    public PageRequest? RequestWithColumns(IReadOnlyList<string> extraColumns)
    {
        var request = RequireRequest();
        var required = QueryColumns.Concat(extraColumns).Distinct(StringComparer.Ordinal).ToList();
        return request.Columns.SequenceEqual(required, StringComparer.Ordinal) ? null : request with { Columns = required };
    }

    public PageCursor NextCursor() => _pager.NextOf(RequireRequest(), LastResult ?? throw NoQuery());

    public PageCursor PreviousCursor() => _pager.PeekPrevious();

    public void MoveNext(PageCursor next) => _pager.Advance(next);

    public void MovePrevious() => _pager.Retreat();

    /// <summary>Every row of the query, real columns only, page by page.</summary>
    public async IAsyncEnumerable<object?[]> ExportRowsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var request = RequireRequest() with { Columns = QueryColumns };
        await foreach (var row in PageStreamer.ReadRowsAsync(FetchPage, request, StreamPageSize, ct))
            yield return row.Values;
    }

    /// <summary>Every row of <paramref name="request"/> as column-name to value, page by page, for the map.</summary>
    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> MapValuesAsync(
        PageRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in PageStreamer.ReadRowsAsync(FetchPage, request, StreamPageSize, ct))
            yield return QueryMapProjection.Values(row.Columns, row.Values);
    }

    /// <summary>The running row total of the query (no COUNT(*)). The request is captured now, not when enumeration starts.</summary>
    public IAsyncEnumerable<RowCountProgress> CountAsync(CancellationToken ct) =>
        RowCounter.CountAsync(FetchPage, RequireRequest() with { Columns = QueryColumns }, CountPageSize, ct);

    private Task<PageResult> FetchPage(PageRequest request, CancellationToken ct) =>
        metadata.GetPageAsync(settings, request, ct);

    private PageRequest RequireRequest() => Request ?? throw NoQuery();

    private static InvalidOperationException NoQuery() => new("No query has been run on this table screen yet.");
}
