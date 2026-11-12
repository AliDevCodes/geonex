using FastDbExplorer.Application.Search;
using FastDbExplorer.Domain;
using Xunit;

namespace FastDbExplorer.Tests;

public sealed class ColumnMatcherTests
{
    [Fact]
    public void Normalize_folds_case_separators_and_persian_forms()
    {
        Assert.Equal(ColumnMatcher.Normalize("National_ID"), ColumnMatcher.Normalize("national id"));
        Assert.Equal("کدملی", ColumnMatcher.Normalize("کد ملی"));
        Assert.Equal(ColumnMatcher.Normalize("كدملي"), ColumnMatcher.Normalize("کد ملی")); // Arabic letters folded
        Assert.Equal(ColumnMatcher.Normalize("کد‌ملی"), ColumnMatcher.Normalize("کدملی")); // zero-width non-joiner
        Assert.Equal("1404", ColumnMatcher.Normalize("۱۴۰۴")); // Persian digits become ASCII digits
    }

    [Fact]
    public void Suggest_ranks_exact_then_synonym_then_substring()
    {
        var columns = new[]
        {
            new ColumnInfo("customer_national_id", "varchar", 0), // contains "nationalid"
            new ColumnInfo("National_ID", "nvarchar", 0),   // exact after normalization
            new ColumnInfo("کد ملی", "nvarchar", 0),        // synonym of national id
            new ColumnInfo("Amount", "decimal", 0),
        };

        var suggestions = ColumnMatcher.Suggest(columns, "national id");

        Assert.Equal(new[] { "National_ID", "کد ملی", "customer_national_id" }, suggestions.Select(s => s.Column.Name).ToArray());
        Assert.Equal(3, suggestions[0].Score);
    }

    [Fact]
    public void Suggest_returns_nothing_for_an_empty_name()
    {
        Assert.Empty(ColumnMatcher.Suggest(new[] { new ColumnInfo("A", "int", 0) }, "  _ "));
    }

    [Theory]
    [InlineData("nvarchar", TypeFamily.Text)]
    [InlineData("bigint", TypeFamily.Numeric)]
    [InlineData("datetime2", TypeFamily.Date)]
    [InlineData("geography", TypeFamily.Other)]
    public void FamilyOf_maps_sql_types(string sqlType, TypeFamily expected)
    {
        Assert.Equal(expected, ColumnMatcher.FamilyOf(sqlType));
    }

    [Fact]
    public void Compare_warns_for_text_against_number_and_blocks_unrelated_types()
    {
        Assert.Equal(MatchQuality.Compatible, ColumnMatcher.Compare(TypeFamily.Text, TypeFamily.Text));
        Assert.Equal(MatchQuality.NeedsConversion, ColumnMatcher.Compare(TypeFamily.Text, TypeFamily.Numeric));
        Assert.Equal(MatchQuality.Incompatible, ColumnMatcher.Compare(TypeFamily.Date, TypeFamily.Numeric));
        Assert.Equal(MatchQuality.Incompatible, ColumnMatcher.Compare(TypeFamily.Other, TypeFamily.Other));
    }
}
