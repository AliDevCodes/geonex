using System.Globalization;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Monitoring;

/// <summary>
/// Reads coordinates from result values. A bad value is a data problem of one row: the methods return false, never throw.
/// </summary>
public static class CoordinateParser
{
    /// <summary>Reads both coordinates of a row. False when either one is NULL, unusable or out of range.</summary>
    public static bool TryReadCoordinates(object?[] row, int latitudeIndex, int longitudeIndex, out double latitude, out double longitude)
    {
        // Both are always parsed (no short-circuit), so both out parameters are assigned on every path.
        var latitudeOk = TryParseCoordinate(row[latitudeIndex], isLatitude: true, out latitude);
        var longitudeOk = TryParseCoordinate(row[longitudeIndex], isLatitude: false, out longitude);
        return latitudeOk && longitudeOk;
    }

    /// <summary>
    /// Makes a coordinate typed in a Persian/Arabic keyboard parse: Persian and Arabic-Indic digits become ASCII,
    /// Arabic decimal separator (U+066B) and Persian comma (U+060C) become '.', and a single comma used as the decimal
    /// separator ("35,7") becomes '.'. Thousands separators are not expected in coordinates.
    /// </summary>
    public static string NormalizeNumericText(string text)
    {
        return PersianDigits.ToAscii(text.Trim())
            .Replace('٫', '.')
            .Replace('،', '.')
            .Replace(',', '.');
    }

    private static bool TryParseCoordinate(object? value, bool isLatitude, out double parsed)
    {
        parsed = 0;
        if (value is null or DBNull) return false;

        try
        {
            parsed = value is string text
                ? double.Parse(NormalizeNumericText(text), NumberStyles.Float, CultureInfo.InvariantCulture)
                : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return false; // not a number: a data problem of this row, not of the check
        }

        return isLatitude ? CoordinateRules.IsValidLatitude(parsed) : CoordinateRules.IsValidLongitude(parsed);
    }
}
