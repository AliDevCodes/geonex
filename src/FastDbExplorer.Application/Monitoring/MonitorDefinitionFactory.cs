using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Monitoring;

/// <summary>The values the monitor editor collected and validated. Pure: no UI.</summary>
public sealed record MonitorDraft(
    string Name,
    string Database,
    string Schema,
    string Table,
    string Watermark,
    IReadOnlyList<FilterCondition> Conditions,
    FilterLogic Logic,
    int IntervalMinutes,
    string OutputFolder,
    bool PlaySound,
    string? LatitudeColumn,
    string? LongitudeColumn,
    IReadOnlyList<Guid>? LayerIds,
    double? DistanceMeters,
    bool LiveMapEnabled,
    bool LiveMapShowAll,
    int LiveMapMinutes,
    string? LiveMapIconKey,
    string? LiveMapColor,
    IReadOnlyList<string>? LiveMapDetailColumns);

/// <summary>
/// Turns a validated draft into a monitor. A new monitor gets a new id and the server it was created on. An edited monitor
/// keeps its id, its server, its table and its watermark column, and takes every other value from the draft.
/// </summary>
public static class MonitorDefinitionFactory
{
    public static MonitorDefinition Create(MonitorDraft draft, string server, MonitorDefinition? existing)
    {
        var coordinateLayerId = draft.LayerIds is { Count: 1 } ? draft.LayerIds[0] : (Guid?)null;
        var liveMapMinutes = LiveResultLifetime.Normalize(draft.LiveMapMinutes);

        if (existing is null)
            return new MonitorDefinition(Guid.NewGuid(), draft.Name, server, draft.Database, draft.Schema, draft.Table,
                draft.Watermark, draft.Conditions, draft.Logic, draft.IntervalMinutes, draft.OutputFolder, draft.PlaySound, true,
                CoordinateLayerId: coordinateLayerId,
                LatitudeColumn: draft.LatitudeColumn, LongitudeColumn: draft.LongitudeColumn, DistanceMeters: draft.DistanceMeters,
                CoordinateLayerIds: draft.LayerIds, LiveMapEnabled: draft.LiveMapEnabled,
                LiveMapIconKey: draft.LiveMapIconKey, LiveMapColor: draft.LiveMapColor,
                LiveMapMinutes: liveMapMinutes, LiveMapShowAllMatches: draft.LiveMapShowAll,
                LiveMapDetailColumns: draft.LiveMapDetailColumns);

        return existing with
        {
            Name = draft.Name,
            Conditions = draft.Conditions,
            Logic = draft.Logic,
            IntervalMinutes = draft.IntervalMinutes,
            OutputFolder = draft.OutputFolder,
            PlaySound = draft.PlaySound,
            CoordinateLayerId = coordinateLayerId,
            LatitudeColumn = draft.LatitudeColumn,
            LongitudeColumn = draft.LongitudeColumn,
            DistanceMeters = draft.DistanceMeters,
            CoordinateLayerIds = draft.LayerIds,
            LiveMapEnabled = draft.LiveMapEnabled,
            LiveMapMinutes = liveMapMinutes,
            LiveMapShowAllMatches = draft.LiveMapShowAll,
            LiveMapIconKey = draft.LiveMapIconKey,
            LiveMapColor = draft.LiveMapColor,
            LiveMapDetailColumns = draft.LiveMapDetailColumns,
        };
    }
}
