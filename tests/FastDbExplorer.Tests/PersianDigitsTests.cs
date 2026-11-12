using FastDbExplorer.Domain;
using Xunit;

namespace FastDbExplorer.Tests;

public sealed class PersianDigitsTests
{
    [Fact]
    public void ToAscii_turns_Persian_and_Arabic_Indic_digits_into_ascii()
    {
        Assert.Equal("1405/08/15 12:30", PersianDigits.ToAscii("۱۴۰۵/۰۸/۱۵ ١٢:٣٠"));
    }

    [Fact]
    public void ToAscii_leaves_other_characters_alone()
    {
        Assert.Equal("a-1.b", PersianDigits.ToAscii("a-۱.b"));
    }

    [Fact]
    public void ToPersian_shows_ascii_digits_as_Persian()
    {
        Assert.Equal("۱۴۰۵/۰۸/۱۵", PersianDigits.ToPersian("1405/08/15"));
    }

    [Fact]
    public void Jalali_date_round_trips_through_text()
    {
        var gregorian = PersianDateTimeCodec.FromPersianCalendar(1405, 8, 15, 0, 0, 0);

        Assert.Equal("1405/08/15", PersianDateTimeCodec.ToJalaliDate(gregorian));
        Assert.True(PersianDateTimeCodec.TryParseJalaliDate("۱۴۰۵/۰۸/۱۵", out var parsed));
        Assert.Equal(gregorian, parsed);
    }

    [Fact]
    public void Jalali_parse_rejects_impossible_dates()
    {
        Assert.False(PersianDateTimeCodec.TryParseJalaliDate("1405/13/01", out _));
        Assert.False(PersianDateTimeCodec.TryParseJalaliDate("1405/08", out _));
        Assert.False(PersianDateTimeCodec.TryParseJalaliDate("abc", out _));
    }
}
