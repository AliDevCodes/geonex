using FastDbExplorer.Application.Search;
using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Search;

namespace FastDbExplorer.Tests;

public class TablePagerTests
{
    private static PageRequest Request(bool keyset, int pageSize) =>
        new("db", "dbo", "t", [], [], FilterLogic.And, [], keyset, PageSize: pageSize, Offset: 0, AfterKey: null);

    private static PageResult Result(object?[]? lastKey) =>
        new(["id"], [], HasMore: true, LastKey: lastKey);

    [Fact]
    public void A_new_pager_starts_at_the_first_page_with_no_history()
    {
        var pager = new TablePager();

        Assert.Equal(0, pager.Current.Offset);
        Assert.Null(pager.Current.AfterKey);
        Assert.False(pager.HasPrevious);
    }

    [Fact]
    public void The_next_page_starts_one_page_further()
    {
        var pager = new TablePager();

        var next = pager.NextOf(Request(keyset: false, pageSize: 100), Result([1]));

        Assert.Equal(100, next.Offset);
        Assert.Null(next.AfterKey);
    }

    [Fact]
    public void Keyset_paging_continues_after_the_last_key_once_past_the_threshold()
    {
        var pager = new TablePager();
        var request = Request(keyset: true, pageSize: 10_000);
        var first = pager.NextOf(request, Result([1]));
        pager.Advance(first);

        var second = pager.NextOf(request, Result([7]));

        Assert.Equal(20_000, second.Offset);
        Assert.Equal(new object?[] { 7 }, second.AfterKey);
    }

    [Fact]
    public void Advancing_then_retreating_returns_to_the_first_page()
    {
        var pager = new TablePager();
        var next = pager.NextOf(Request(keyset: false, pageSize: 50), Result(null));

        pager.Advance(next);
        Assert.True(pager.HasPrevious);
        Assert.Equal(50, pager.Current.Offset);

        Assert.Equal(0, pager.PeekPrevious().Offset);
        pager.Retreat();

        Assert.Equal(0, pager.Current.Offset);
        Assert.False(pager.HasPrevious);
    }

    [Fact]
    public void Reset_forgets_the_history()
    {
        var pager = new TablePager();
        pager.Advance(pager.NextOf(Request(keyset: false, pageSize: 50), Result(null)));

        pager.Reset();

        Assert.Equal(0, pager.Current.Offset);
        Assert.False(pager.HasPrevious);
    }
}
