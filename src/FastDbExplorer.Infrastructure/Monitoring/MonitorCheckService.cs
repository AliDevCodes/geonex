using FastDbExplorer.Application.Abstractions;
using FastDbExplorer.Application.Monitoring;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Infrastructure.Monitoring;

/// <summary>
/// One check = "which records appeared since the last check, and which of them match the user's conditions?".
/// Steps: read MAX(watermark) once, query only the window (last, upper] with the conditions on the server,
/// page through scalar matches (OFFSET, ordered by watermark + primary key); proximity rules are then checked on each
/// explicit-column page before applying the report cap.
/// The first check only records the starting point, so old data never floods the user with alerts.
/// For date watermarks the newest <see cref="MonitorDefinition.SettleSeconds"/> are held back (see <see cref="SettleUpper"/>).
///
/// NULL and unusable coordinates: a record whose latitude or longitude is NULL, missing, not a number or out of range
/// is never a proximity match (it cannot be near a layer), but it does not stop the check. When the live map option is
/// on, such a record is still reported and only its map point is skipped (counted in
/// <see cref="MonitorCheckResult.MapSkippedRows"/>). A database or configuration error still fails the check.
///
/// The pieces: <see cref="MonitorColumnPlan"/> chooses the columns, <see cref="WindowScan"/> pages and applies the cap,
/// <see cref="ProximityMatcher"/> applies the proximity rule and <see cref="LiveMapCollector"/> builds the map points.
/// </summary>
public sealed class MonitorCheckService(
    IDatabaseMetadataService metadata, IWatermarkReader watermarks, IReportWriter reports, ICoordinateLayerStore coordinateLayers)
    : IMonitorCheckService
{
    public const int PageSize = 1000;
    public const int MaxRows = 10_000;
    private const string NearestPointColumn = "Nearest point name";
    private const string CoordinateLayerColumn = "Coordinate layer";
    private const string DistanceColumn = "Distance (m)";

    public async Task<MonitorCheckResult> CheckAsync(ConnectionSettings settings, MonitorDefinition monitor, CancellationToken ct)
    {
        var checkedAt = DateTime.UtcNow;
        var layers = await LoadProximityLayersAsync(monitor, ct);
        var columns = await metadata.GetColumnsAsync(settings, monitor.Database, monitor.Schema, monitor.Table, ct);
        var watermark = ResolveWatermark(monitor, columns);
        var plan = MonitorColumnPlan.Build(monitor, columns, layers);

        var reading = await watermarks.ReadAsync(settings, monitor.Database, monitor.Schema, monitor.Table, watermark.Name, ct);
        var upper = SettleUpper(reading, watermark.TypeName, monitor.SettleSeconds);

        // First successful check: remember where we are, do not alert for existing data.
        if (monitor.LastWatermark is null && monitor.LastCheckedUtc is null)
            return new MonitorCheckResult(monitor.Id, checkedAt, plan.ReportBase, [], false, upper, null, true);

        var nothingNew = upper is null
                         || (monitor.LastWatermark is not null
                             && WatermarkComparer.Compare(upper, monitor.LastWatermark, watermark.TypeName) <= 0);
        if (nothingNew)
            return new MonitorCheckResult(monitor.Id, checkedAt, plan.ReportBase, [], false, monitor.LastWatermark, null, false);

        var scan = new WindowScan(metadata, settings, monitor, watermark, plan, layers);
        await scan.RunAsync(WindowConditions(monitor, watermark.Name, upper), WindowOrder(columns, watermark.Name), ct);

        IReadOnlyList<string> reportColumns = layers.Count == 0
            ? plan.ReportBase
            : plan.ReportBase.Concat([NearestPointColumn, CoordinateLayerColumn, DistanceColumn]).ToList();
        string? reportPath = null;
        if (scan.Rows.Count > 0)
            reportPath = await reports.WriteAsync(monitor.Name, checkedAt, reportColumns, scan.Rows, monitor.OutputFolder, ct);

        return new MonitorCheckResult(monitor.Id, checkedAt, reportColumns, scan.Rows, scan.Truncated,
            scan.Truncated ? scan.CapWatermark : upper, reportPath, false,
            plan.LiveMap ? scan.Map.Points : null, scan.Map.Skipped);
    }

    /// <summary>
    /// Every layer the proximity rule watches, in the order the user selected them. The rule is independent of the
    /// user conditions: it can be the only rule of a monitor (see <see cref="MonitorDefinition.HasEffectiveRule"/>).
    /// </summary>
    private async Task<IReadOnlyList<MapLayer>> LoadProximityLayersAsync(MonitorDefinition monitor, CancellationToken ct)
    {
        var ids = monitor.SelectedCoordinateLayerIds;
        if (ids.Count == 0 && !monitor.HasProximityRule) return [];
        if (ids.Count == 0)
            throw new InvalidOperationException($"Monitor '{monitor.Name}' has a proximity rule without a saved coordinate layer. Reopen it and select at least one layer.");

        if (!monitor.HasCoordinateColumns
            || monitor.DistanceMeters is not { } distance || !double.IsFinite(distance) || distance <= 0
            || distance > CoordinateRules.MaxRadiusMeters)
            throw new InvalidOperationException($"Monitor '{monitor.Name}' has an incomplete or invalid proximity rule. Reopen it and select distinct numeric coordinate columns, and a distance greater than 0 and no greater than {CoordinateRules.MaxRadiusMeters} metres.");

        var all = await coordinateLayers.LoadAllAsync(ct);
        var layers = new List<MapLayer>(ids.Count);
        foreach (var layerId in ids)
        {
            var layer = all.FirstOrDefault(item => item.Id == layerId);
            if (layer is null)
                throw new InvalidOperationException($"Monitor '{monitor.Name}' refers to a missing or unreadable imported coordinate layer ({layerId}). Choose an available layer in the monitor editor.");
            if (layer.Points.Count == 0)
                throw new InvalidOperationException($"The selected coordinate layer '{layer.Name}' contains no points and cannot be used by monitor '{monitor.Name}'.");
            if (layer.Points.Any(point => !CoordinateRules.IsValidLatitude(point.Latitude) || !CoordinateRules.IsValidLongitude(point.Longitude)))
                throw new InvalidOperationException($"The selected coordinate layer '{layer.Name}' contains invalid WGS84 coordinates.");
            layers.Add(layer);
        }
        return layers;
    }

    /// <summary>A watermark column that is missing, or of a type that cannot order new records, fails the check.</summary>
    private static ColumnInfo ResolveWatermark(MonitorDefinition monitor, IReadOnlyList<ColumnInfo> columns)
    {
        var watermark = columns.FirstOrDefault(c => c.Name == monitor.WatermarkColumn)
                        ?? throw new ArgumentException($"Column '{monitor.WatermarkColumn}' no longer exists in {monitor.TargetText}.");
        if (!WatermarkTypes.IsSupported(watermark.TypeName))
            throw new NotSupportedException($"Column '{watermark.Name}' ({watermark.TypeName}) cannot be used to detect new records.");
        return watermark;
    }

    /// <summary>(last, upper]. No lower bound only when the table was empty at the first check.</summary>
    private static IReadOnlyList<FilterCondition> WindowConditions(MonitorDefinition monitor, string watermarkColumn, string? upper)
    {
        var conditions = new List<FilterCondition>();
        if (monitor.LastWatermark is not null)
            conditions.Add(new FilterCondition(watermarkColumn, FilterOperator.GreaterThan, monitor.LastWatermark));
        conditions.Add(new FilterCondition(watermarkColumn, FilterOperator.LessOrEqual, upper));
        return conditions;
    }

    /// <summary>Watermark first, then the primary key: a deterministic order that paging and the cap rely on.</summary>
    private static IReadOnlyList<string> WindowOrder(IReadOnlyList<ColumnInfo> columns, string watermarkColumn)
    {
        var order = new List<string> { watermarkColumn };
        order.AddRange(columns.Where(c => c.IsPrimaryKey && c.IsFilterable && c.Name != watermarkColumn)
            .OrderBy(c => c.KeyOrdinal).Select(c => c.Name));
        return order;
    }

    /// <summary>
    /// Upper end of the checked window. A row with a slightly older date can be committed a moment after a newer one
    /// (parallel transactions); if the window ended exactly at MAX such a row would be skipped forever. So for date
    /// columns the end is min(MAX, server time - settle). It can only delay a row (never lose it): an idle table is
    /// still reported once the settle time has passed, and a column in another time zone just waits longer.
    /// Number columns (identity) have no clock, so they are used as they are.
    /// </summary>
    public static string? SettleUpper(WatermarkReading reading, string typeName, int settleSeconds)
    {
        if (reading.Max is null || settleSeconds <= 0 || reading.ServerNow is not { } now || !WatermarkTypes.IsDate(typeName))
            return reading.Max;

        var limit = WatermarkTypes.Format(now.AddSeconds(-settleSeconds));
        return WatermarkComparer.Compare(reading.Max, limit, typeName) > 0 ? limit : reading.Max;
    }
}
