using FastDbExplorer.Application.Abstractions;
using FastDbExplorer.Application.Monitoring;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Infrastructure.Monitoring;

/// <summary>
/// Pages through one check window (the rows after the last check, up to the upper bound) and keeps the rows that match.
/// Rows arrive in watermark order. Once <see cref="MonitorCheckService.MaxRows"/> rows are kept, the scan goes on only
/// through the rows that share the last watermark value; the first row beyond that group ends the scan and marks the
/// result as truncated, so the next check resumes after a complete group.
/// </summary>
internal sealed class WindowScan(
    IDatabaseMetadataService metadata, ConnectionSettings settings, MonitorDefinition monitor, ColumnInfo watermark,
    MonitorColumnPlan plan, IReadOnlyList<MapLayer> layers)
{
    private readonly List<object?[]> _rows = [];
    private readonly LiveMapCollector _map = new();
    private bool _capReached;
    private bool _truncated;
    private string? _capWatermark;

    /// <summary>The report rows, in watermark order.</summary>
    public IReadOnlyList<object?[]> Rows => _rows;

    /// <summary>The live map points of this window and the count of rows that could not be placed.</summary>
    public LiveMapCollector Map => _map;

    /// <summary>True when the window held more matches than the cap allows.</summary>
    public bool Truncated => _truncated;

    /// <summary>Watermark of the last complete group that was kept, once the cap is reached.</summary>
    public string? CapWatermark => _capWatermark;

    public async Task RunAsync(IReadOnlyList<FilterCondition> required, IReadOnlyList<string> order, CancellationToken ct)
    {
        var watermarkIndex = plan.Selected.ToList().IndexOf(watermark.Name);
        for (long offset = 0; ; offset += MonitorCheckService.PageSize)
        {
            ct.ThrowIfCancellationRequested();
            var request = new PageRequest(
                monitor.Database, monitor.Schema, monitor.Table, plan.Selected, monitor.Conditions, monitor.Logic,
                order, false, MonitorCheckService.PageSize, offset, null, required);
            var page = await metadata.GetPageAsync(settings, request, ct);

            for (var i = 0; i < page.Rows.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (!Accept(page.Rows[i], offset + i + 1, watermarkIndex, ct)) return;
            }
            if (!page.HasMore) return;
        }
    }

    /// <summary>Handles one candidate row. Returns false when the scan must stop (the first row beyond a complete cap group).</summary>
    private bool Accept(object?[] candidate, long rowNumber, int watermarkIndex, CancellationToken ct)
    {
        var candidateWatermark = candidate[watermarkIndex] is null or DBNull
            ? throw new InvalidOperationException($"Monitor '{monitor.Name}' found a candidate without a watermark at row {rowNumber}.")
            : WatermarkTypes.Format(candidate[watermarkIndex]!);

        if (_capReached)
        {
            var comparison = WatermarkComparer.Compare(candidateWatermark, _capWatermark!, watermark.TypeName);
            if (comparison > 0)
            {
                // Resume after a complete watermark group; a strict > lower bound must not split ties.
                _truncated = true;
                return false;
            }
            if (comparison < 0)
                throw new InvalidOperationException($"Monitor '{monitor.Name}' received rows out of watermark order while checking a capped result.");
        }

        // Read once per row. A row without usable coordinates is simply not located; it never ends the check.
        // Declared before the call: out variables inside '&&' are not definitely assigned for the code below.
        double latitude = 0, longitude = 0;
        var located = plan.NeedCoordinates
                      && CoordinateParser.TryReadCoordinates(candidate, plan.LatitudeIndex, plan.LongitudeIndex, out latitude, out longitude);

        var matched = ProximityMatcher.Match(candidate, layers, located, latitude, longitude, monitor.DistanceMeters ?? 0, ct);
        if (matched is null)
        {
            // Not a report row. It may still be a map point when the user asked for all matching records.
            if (plan.LiveAll && located)
                _map.Add(candidate, candidateWatermark, plan, latitude, longitude);
            return true;
        }

        if (_capReached)
            throw new InvalidOperationException($"Monitor '{monitor.Name}' matched more than {MonitorCheckService.MaxRows:N0} rows with the same watermark value. It cannot safely resume without skipping records; choose a watermark column with fewer ties or split the monitor conditions.");

        _rows.Add(ReportRow(matched, plan.ReportWidth, layers.Count > 0 ? ProximityMatcher.ExtraColumns : 0));
        if (plan.LiveMap)
        {
            if (located) _map.Add(candidate, candidateWatermark, plan, latitude, longitude);
            else _map.CountSkipped();
        }

        if (_rows.Count == MonitorCheckService.MaxRows)
        {
            _capReached = true;
            _capWatermark = candidateWatermark;
        }
        return true;
    }

    /// <summary>A matched row without the map-only columns: the table's columns, then the proximity columns when a layer is watched.</summary>
    private static object?[] ReportRow(object?[] row, int reportWidth, int extraColumns)
    {
        if (row.Length == reportWidth + extraColumns) return row;

        var trimmed = new object?[reportWidth + extraColumns];
        Array.Copy(row, trimmed, reportWidth);
        Array.Copy(row, row.Length - extraColumns, trimmed, reportWidth, extraColumns);
        return trimmed;
    }
}
