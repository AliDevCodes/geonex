using System.Globalization;
using System.Text;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Search;

/// <summary>
/// Finds the column that plays the same role in another database: same normalized name, a known synonym (for example
/// "کد ملی" and "national_id"), or a name that contains the wanted one. Nothing here touches a database; the user
/// confirms every suggestion before a search runs.
/// </summary>
public static class ColumnMatcher
{
    // Groups of names that mean the same thing. Names are compared after normalization (see Normalize).
    private static readonly string[][] SynonymGroups =
    [
        ["nationalid", "nid", "nationalcode", "codemeli", "melicode", "کدملی", "شمارهملی", "کدمحلی"],
        ["mobile", "mobileno", "mobilenumber", "phone", "phonenumber", "موبایل", "شمارهموبایل", "تلفن", "شمارهتماس"],
        ["email", "mail", "ایمیل", "پست‌الکترونیک"],
        ["city", "town", "شهر"],
        ["postalcode", "zipcode", "zip", "کدپستی"],
    ];

    /// <summary>
    /// Lower-case, letters and digits only, Persian digits and Arabic letters folded to the Persian forms, and the
    /// zero-width non-joiner removed. "کد_ملی" and "Kd Meli" stay different, on purpose: only synonyms bridge them.
    /// </summary>
    public static string Normalize(string name)
    {
        var builder = new StringBuilder(name.Length);
        var text = PersianDigits.ToAscii(name.Normalize(NormalizationForm.FormKC).ToLowerInvariant());
        foreach (var ch in text)
        {
            var folded = ch switch
            {
                'ي' => 'ی',
                'ك' => 'ک',
                '‌' => '\0',
                _ => ch
            };
            if (folded == '\0') continue;
            if (char.IsLetterOrDigit(folded)) builder.Append(folded);
        }
        return builder.ToString();
    }

    /// <summary>Candidate columns for one wanted name, best first. Exact matches rank above synonyms, synonyms above substrings.</summary>
    public static IReadOnlyList<(ColumnInfo Column, int Score)> Suggest(IEnumerable<ColumnInfo> columns, string wantedName)
    {
        var wanted = Normalize(wantedName);
        if (wanted.Length == 0) return [];

        var group = SynonymGroups.FirstOrDefault(g => g.Contains(wanted));
        var result = new List<(ColumnInfo, int)>();
        foreach (var column in columns)
        {
            var candidate = Normalize(column.Name);
            if (candidate.Length == 0) continue;

            int score;
            if (candidate == wanted) score = 3;
            else if (group is not null && group.Contains(candidate)) score = 2;
            else if (wanted.Length >= 4 && candidate.Length >= 4 && (candidate.Contains(wanted) || wanted.Contains(candidate))) score = 1;
            else continue;

            result.Add((column, score));
        }
        return result.OrderByDescending(r => r.Item2).ThenBy(r => r.Item1.Name, StringComparer.Ordinal).ToList();
    }

    public static TypeFamily FamilyOf(string typeName)
    {
        var t = typeName.Trim().ToLowerInvariant();
        return t switch
        {
            "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" => TypeFamily.Text,
            "tinyint" or "smallint" or "int" or "bigint" or "decimal" or "numeric" or "float" or "real" or "money" or "smallmoney" => TypeFamily.Numeric,
            "date" or "datetime" or "datetime2" or "smalldatetime" => TypeFamily.Date,
            _ => TypeFamily.Other
        };
    }

    /// <summary>
    /// Whether a search value written for a column of the first family can be compared with a column of the second.
    /// Text against number is allowed with a warning, because leading zeros (a national code like 0012345678) are lost
    /// in a numeric column and the value must be converted in the same way on both sides.
    /// </summary>
    public static MatchQuality Compare(TypeFamily source, TypeFamily target)
    {
        if (source == target && source != TypeFamily.Other) return MatchQuality.Compatible;
        if ((source == TypeFamily.Text && target == TypeFamily.Numeric) || (source == TypeFamily.Numeric && target == TypeFamily.Text))
            return MatchQuality.NeedsConversion;
        return MatchQuality.Incompatible;
    }
}
