using System.Globalization;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Wpf.ViewModels;

/// <summary>One cell keeps its raw value for export and a readable value for the grid.</summary>
public sealed record GridCell(string Display, string FullValue, object? RawValue, bool IsTruncated);

/// <summary>The grid binds to each row by index: [0], [1], ...; map-only values stay out of the grid/export.</summary>
public sealed class GridRow(GridCell[] cells, IReadOnlyDictionary<string, object?>? mapValues = null)
{
    public GridCell this[int index] => cells[index];
    public IReadOnlyList<GridCell> Cells => cells;
    public IReadOnlyDictionary<string, object?> MapValues { get; private set; } = mapValues ?? new Dictionary<string, object?>();

    internal void UpdateMapValues(IReadOnlyDictionary<string, object?> values) => MapValues = values;
}

/// <summary>Turns a raw database value into the text the grid shows and the full text the cell keeps.</summary>
public static class GridCellFactory
{
    private const int MaxDisplayLength = 160;
    private const int MaxBinaryBytes = 16;

    public static GridCell Create(object? value)
    {
        var full = value switch
        {
            null => "NULL",
            string s => s,
            DateTime d => FormatDateTime(d),
            byte[] b => "0x" + Convert.ToHexString(b.AsSpan(0, Math.Min(b.Length, MaxBinaryBytes)))
                + (b.Length > MaxBinaryBytes ? "…" : ""),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
        var truncated = value is string text && text.Length > MaxDisplayLength;
        return new GridCell(truncated ? full[..MaxDisplayLength] + "…" : full, full, value, truncated);
    }

    private static string FormatDateTime(DateTime value)
    {
        try
        {
            var display = PersianDateTimeCodec.ToPersianDisplay(value);
            if (value.TimeOfDay == TimeSpan.Zero) display = display[..10];
            return PersianDigits.ToPersian(display);
        }
        catch (ArgumentOutOfRangeException)
        {
            return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }
}
