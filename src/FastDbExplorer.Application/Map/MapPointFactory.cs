using System.Globalization;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Map;

/// <summary>
/// Builds the points sent to the map from result values. Coordinates are checked (Persian and Arabic digits are
/// converted first), and names and detail texts are shortened so the map card stays readable.
/// Pure: no UI and no database, so it can be tested on its own.
/// </summary>
public static class MapPointFactory
{
    public const int MaxNameLength = 100;
    public const int MaxDetailColumns = 8;
    public const int MaxDetailKeyLength = 80;
    public const int MaxDetailValueLength = 240;
    public const int MaxDetailTotalLength = 1_200;

    /// <summary>True when both values are numbers inside the valid latitude and longitude ranges.</summary>
    public static bool TryReadCoordinates(object? latitudeValue, object? longitudeValue, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        return TryParseCoordinate(latitudeValue, out latitude)
            && TryParseCoordinate(longitudeValue, out longitude)
            && CoordinateRules.IsValidLatitude(latitude)
            && CoordinateRules.IsValidLongitude(longitude);
    }

    /// <summary>A point with a name. An empty name becomes "Row n" (the row's position in the result), and the name is shortened.</summary>
    public static QueryMapPoint Create(string? name, int rowNumber, double latitude, double longitude,
        IReadOnlyDictionary<string, string>? details)
    {
        var text = string.IsNullOrWhiteSpace(name) ? $"Row {rowNumber}" : name;
        return new QueryMapPoint(ShortenText(text, MaxNameLength), latitude, longitude, details);
    }

    /// <summary>
    /// Detail texts in the given column order. At most <see cref="MaxDetailColumns"/> columns are read; a column whose
    /// text is null is skipped; keys, values and the whole list are capped in length. Null when nothing is left.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? BuildDetails(IEnumerable<string> columns, Func<string, string?> textOf)
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal);
        var totalLength = 0;
        foreach (var column in columns.Take(MaxDetailColumns))
        {
            var text = textOf(column);
            if (text is null) continue;
            var key = ShortenText(column, MaxDetailKeyLength);
            var remaining = MaxDetailTotalLength - totalLength - key.Length;
            if (remaining <= 0) break;
            var value = ShortenText(text, Math.Min(MaxDetailValueLength, remaining));
            details[key] = value;
            totalLength += key.Length + value.Length;
        }
        return details.Count == 0 ? null : details;
    }

    /// <summary>Cuts the text to <paramref name="maxLength"/> characters with an ellipsis, without splitting a surrogate pair.</summary>
    public static string ShortenText(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        var length = Math.Max(0, maxLength - 1);
        if (length > 0 && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
            length--;
        return value[..length] + "…";
    }

    private static bool TryParseCoordinate(object? value, out double coordinate)
    {
        coordinate = 0;
        if (value is null or DBNull or bool or DateTime or DateTimeOffset or DateOnly or TimeOnly) return false;
        try
        {
            if (value is string or char)
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture)!.Trim();
                text = string.Concat(text.Select(character =>
                {
                    var digit = CharUnicodeInfo.GetDecimalDigitValue(character);
                    if (digit is >= 0 and <= 9) return (char)('0' + digit);
                    return character == '\u066B' ? '.' : character; // Arabic decimal separator
                }));
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out coordinate)
                       && double.IsFinite(coordinate);
            }
            if (value is not IConvertible) return false;
            coordinate = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsFinite(coordinate);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return false;
        }
    }
}
