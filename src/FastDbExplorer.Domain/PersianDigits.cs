using System.Linq;

namespace FastDbExplorer.Domain;

/// <summary>
/// Converts between ASCII digits and the Persian (U+06F0..U+06F9) and Arabic-Indic (U+0660..U+0669) digits that a
/// Persian keyboard types. Every other character is left as it is.
/// </summary>
public static class PersianDigits
{
    /// <summary>Shows ASCII digits as Persian digits (for display only; never for SQL or parsing).</summary>
    public static string ToPersian(string value) => string.Concat(value.Select(c =>
        c is >= '0' and <= '9' ? (char)('۰' + c - '0') : c));

    /// <summary>Turns Persian and Arabic-Indic digits into ASCII digits so the text can be parsed.</summary>
    public static string ToAscii(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is >= '۰' and <= '۹') chars[i] = (char)('0' + (c - '۰'));
            else if (c is >= '٠' and <= '٩') chars[i] = (char)('0' + (c - '٠'));
        }
        return new string(chars);
    }
}
