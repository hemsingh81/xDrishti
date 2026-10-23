using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>Spec P7-15: the ISO week that keys a digest.</summary>
public sealed class DigestWeekTests
{
    [Fact]
    public void A_week_runs_from_monday_to_sunday()
    {
        var week = DigestWeek.TryParse("2026-W43")!.Value;

        week.Start.ShouldBe(new DateOnly(2026, 10, 19));
        week.End.ShouldBe(new DateOnly(2026, 10, 25));
        week.Key.ShouldBe("2026-W43");
    }

    [Theory]
    [InlineData("2026-10-23", "2026-W43")]
    [InlineData("2026-10-25", "2026-W43")]
    [InlineData("2026-12-31", "2026-W53")]
    [InlineData("2027-01-01", "2026-W53")]
    [InlineData("2027-01-04", "2027-W01")]
    public void A_date_falls_in_its_iso_week(string date, string expected)
    {
        DigestWeek.Of(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)).Key.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-43")]
    [InlineData("2026-W00")]
    [InlineData("2026-W54")]
    [InlineData("2027-W53")]
    [InlineData("26-W43-1")]
    [InlineData("abcd-W43")]
    public void Text_that_is_not_a_week_is_refused(string? text)
    {
        DigestWeek.TryParse(text).ShouldBeNull();
    }
}
