using FastDbExplorer.Application.Map;
using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Map;

namespace FastDbExplorer.Tests;

public class MapCollectorTests
{
    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await Task.Yield();
            yield return new Dictionary<string, object?> { ["id"] = i };
        }
    }

    /// <summary>A valid point for even ids, null for odd ids.</summary>
    private static QueryMapPoint? EvenOnly(IReadOnlyDictionary<string, object?> values, int rowNumber) =>
        (int)values["id"]! % 2 == 0 ? new QueryMapPoint($"row {rowNumber}", 35.7, 51.4) : null;

    [Fact]
    public async Task Valid_rows_become_points_and_invalid_rows_are_skipped()
    {
        var result = await MapCollector.CollectAsync(Rows(6), EvenOnly, maxPoints: 100, onProgress: _ => { });

        Assert.Equal(3, result.Points.Count);
        Assert.Equal(3, result.Skipped);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Reaching_the_cap_stops_collection_and_marks_it_truncated()
    {
        var result = await MapCollector.CollectAsync(Rows(10), (_, _) => new QueryMapPoint("p", 35.7, 51.4),
            maxPoints: 2, onProgress: _ => { });

        Assert.Equal(2, result.Points.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task Hitting_the_cap_exactly_with_no_more_valid_rows_is_not_truncated()
    {
        var result = await MapCollector.CollectAsync(Rows(2), (_, _) => new QueryMapPoint("p", 35.7, 51.4),
            maxPoints: 2, onProgress: _ => { });

        Assert.Equal(2, result.Points.Count);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Progress_is_reported_every_thousand_scanned_rows()
    {
        var reported = new List<int>();
        await MapCollector.CollectAsync(Rows(2500), (_, _) => new QueryMapPoint("p", 35.7, 51.4),
            maxPoints: 10_000, onProgress: reported.Add);

        Assert.Equal(new[] { 1000, 2000 }, reported);
    }

    [Fact]
    public async Task Progress_is_not_reported_for_rows_that_were_skipped_at_that_count()
    {
        var reported = new List<int>();
        await MapCollector.CollectAsync(Rows(1000), (values, _) =>
                (int)values["id"]! == 1000 ? null : new QueryMapPoint("p", 35.7, 51.4),
            maxPoints: 10_000, onProgress: reported.Add);

        Assert.Empty(reported);
    }
}
