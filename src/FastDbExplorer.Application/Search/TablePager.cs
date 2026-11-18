using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Search;

namespace FastDbExplorer.Application.Search;

/// <summary>
/// Remembers where the table screen is: the current page, and the pages before it so Previous can go back.
/// It does not fetch anything; the caller fetches a cursor first and records it only when the fetch succeeded.
/// Pure: no UI, no database.
/// </summary>
public sealed class TablePager
{
    private readonly Stack<PageCursor> _history = new();

    public static PageCursor First => new(0, null);

    public PageCursor Current { get; private set; } = First;

    public bool HasPrevious => _history.Count > 0;

    /// <summary>Back to the first page with no history. Called when a new query starts.</summary>
    public void Reset()
    {
        _history.Clear();
        Current = First;
    }

    /// <summary>The cursor of the page after the current one, using the last result's key for keyset paging.</summary>
    public PageCursor NextOf(PageRequest request, PageResult lastResult)
    {
        var offset = Current.Offset + request.PageSize;
        return new PageCursor(offset, PageStreamer.AfterKeyFor(request, offset, lastResult.LastKey));
    }

    /// <summary>The page we would return to. Only valid when <see cref="HasPrevious"/>.</summary>
    public PageCursor PeekPrevious() => _history.Peek();

    /// <summary>Moves forward: the current page goes on the history and <paramref name="next"/> becomes current.</summary>
    public void Advance(PageCursor next)
    {
        _history.Push(Current);
        Current = next;
    }

    /// <summary>Moves back to the page that <see cref="PeekPrevious"/> returned.</summary>
    public void Retreat() => Current = _history.Pop();
}
