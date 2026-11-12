using System.Globalization;

namespace FastDbExplorer.Domain;

public static class PersianDateTimeCodec
{
    public const string DatabaseFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly string[] DatabaseFormats =
    [
        DatabaseFormat,
        "yyyy-MM-dd HH:mm:ss.FFFFFFF"
    ];

    public static string ToDatabaseValue(DateTime value) =>
        value.ToString(DatabaseFormat, CultureInfo.InvariantCulture);

    public static bool TryParseDatabaseValue(string? value, out DateTime result) =>
        DateTime.TryParseExact(value, DatabaseFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out result);

    public static DateTime FromPersianCalendar(
        int year, int month, int day, int hour, int minute, int second) =>
        new PersianCalendar().ToDateTime(year, month, day, hour, minute, second, 0);

    public static (int Year, int Month, int Day) ToPersianCalendar(DateTime value)
    {
        var calendar = new PersianCalendar();
        return (calendar.GetYear(value), calendar.GetMonth(value), calendar.GetDayOfMonth(value));
    }

    public static string ToPersianDisplay(DateTime value)
    {
        var (year, month, day) = ToPersianCalendar(value);
        var culture = CultureInfo.InvariantCulture;
        return $"{year.ToString("D4", culture)}/{month.ToString("D2", culture)}/{day.ToString("D2", culture)} {value.ToString("HH:mm:ss", culture)}";
    }

    /// <summary>Formats the date part as a Jalali string such as 1405/08/15 (ASCII digits).</summary>
    public static string ToJalaliDate(DateTime date)
    {
        var (year, month, day) = ToPersianCalendar(date);
        return $"{year:0000}/{month:00}/{day:00}";
    }

    /// <summary>
    /// Parses a Jalali date such as 1405/08/15. Persian and Arabic-Indic digits are accepted. Returns the Gregorian
    /// midnight of that day, or false when the text is not a valid Jalali date.
    /// </summary>
    public static bool TryParseJalaliDate(string text, out DateTime date)
    {
        date = default;
        var parts = PersianDigits.ToAscii(text).Trim().Split('/');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
            return false;
        try
        {
            date = FromPersianCalendar(year, month, day, 0, 0, 0);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
