using System.Globalization;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Monitoring;

/// <summary>
/// Collects the live map points of one check, and counts the report rows that could not be placed on the map.
/// </summary>
public sealed class LiveMapCollector
{
    public const int MaxNameLength = 256;
    public const int MaxDetailValueLength = 240;

    private readonly List<LiveMapPoint> _points = [];
    private readonly Dictionary<string, int> _pointKeys = new(StringComparer.Ordinal);

    /// <summary>Points collected so far, in scan order.</summary>
    public IReadOnlyList<LiveMapPoint> Points => _points;

    /// <summary>Report rows that had no usable coordinates, so no map point was made for them.</summary>
    public int Skipped { get; private set; }

    /// <summary>
    /// Adds one matched revision of a record as a map point. The identity is its primary key plus its watermark value, so
    /// an updated record that matches again is a new point, exactly like the new match the notification reports. Two
    /// identical rows (no primary key) in the same check get a counter, so they stay two points.
    /// </summary>
    public void Add(object?[] row, string watermark, MonitorColumnPlan plan, double latitude, double longitude)
    {
        var recordKey = string.Join('\u001f', plan.KeyIndexes.Select(index => FormatPart(row[index])));
        var baseKey = watermark + '\u001e' + recordKey;
        var occurrence = _pointKeys.TryGetValue(baseKey, out var seen) ? seen + 1 : 0;
        _pointKeys[baseKey] = occurrence;
        var key = occurrence == 0 ? baseKey : baseKey + '\u001d' + occurrence;

        string? name = row.Select(value => value as string).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))?.Trim();
        if (name is null) name = recordKey; // the record's own key, never the watermark-prefixed point key
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];

        var details = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var index in plan.DetailIndexes)
        {
            var text = FormatPart(row[index]);
            if (text.Length > MaxDetailValueLength) text = text[..MaxDetailValueLength];
            details[plan.Selected[index]] = text;
        }

        _points.Add(new LiveMapPoint(key, name, latitude, longitude, details));
    }

    /// <summary>Counts one report row that could not be placed on the map.</summary>
    public void CountSkipped() => Skipped++;

    private static string FormatPart(object? value) => value switch
    {
        null or DBNull => "NULL",
        byte[] bytes => Convert.ToHexString(bytes),
        DateTime date => WatermarkTypes.Format(date),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? ""
    };
}
