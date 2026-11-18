using System.Collections.Generic;
using System.Threading.Tasks;
using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Map;

namespace FastDbExplorer.Application.Map;

/// <summary>The points of a whole search for the map, and how the collection ended.</summary>
public sealed record MapCollection(IReadOnlyList<QueryMapPoint> Points, int Skipped, bool Truncated);

/// <summary>
/// Collects map points from streamed rows, up to a maximum. Rows that do not become a point are counted as skipped.
/// Reports progress every <see cref="ProgressEvery"/> rows that were scanned. Pure: no UI, no database.
/// </summary>
public static class MapCollector
{
    public const int ProgressEvery = 1000;

    /// <param name="rows">The streamed rows of the search.</param>
    /// <param name="project">Turns one row (and its 1-based number) into a point, or null when it has no valid coordinates.</param>
    /// <param name="maxPoints">The most points to collect. One more valid row after that marks the collection as truncated.</param>
    /// <param name="onProgress">Called with the number of rows scanned so far.</param>
    public static async Task<MapCollection> CollectAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        Func<IReadOnlyDictionary<string, object?>, int, QueryMapPoint?> project,
        int maxPoints,
        Action<int> onProgress)
    {
        var points = new List<QueryMapPoint>();
        var scanned = 0;
        var skipped = 0;
        await foreach (var values in rows)
        {
            scanned++;
            var point = project(values, scanned);
            if (point is null)
            {
                skipped++;
                continue;
            }
            if (points.Count == maxPoints) return new MapCollection(points, skipped, Truncated: true);
            points.Add(point);
            if (scanned % ProgressEvery == 0) onProgress(scanned);
        }
        return new MapCollection(points, skipped, Truncated: false);
    }
}
