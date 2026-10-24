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

    private static DateTimeOffset Ist(string iso) => DateTimeOffset.Parse(iso + "+05:30", System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2026-W43", "2026-10-24T10:00:00", false)]
    [InlineData("2026-W43", "2026-10-24T09:59:59", true)]
    [InlineData("2026-W43", "2026-10-26T12:00:00", false)]
    [InlineData("2026-W43", "2026-10-31T10:00:00", false)]
    [InlineData("2026-W43", "2026-11-02T12:00:00", true)]
    [InlineData("2026-W42", "2026-10-26T12:00:00", true)]
    [InlineData("2026-W42", "2026-10-17T09:00:00", true)]
    [InlineData("2026-W43", "2026-10-21T12:00:00", true)]
    [InlineData("2026-W44", "2026-10-24T12:00:00", true)]
    public void Only_the_current_week_from_saturday_10_or_the_last_completed_week_may_be_stored(string week, string now, bool refused)
    {
        var refusal = DigestWeek.TryParse(week)!.Value.StoreRefusal(Ist(now));

        (refusal is not null).ShouldBe(refused);
    }

    [Fact]
    public void The_week_follows_the_india_date_not_the_utc_date()
    {
        var newYear = new DateTimeOffset(2026, 12, 31, 20, 0, 0, TimeSpan.Zero);
        var sunday = new DateTimeOffset(2026, 10, 25, 20, 0, 0, TimeSpan.Zero);

        DigestWeek.Of(XDrishti.Domain.Market.IndiaTime.Today(newYear)).Key.ShouldBe("2026-W53");
        DigestWeek.Of(XDrishti.Domain.Market.IndiaTime.Today(sunday)).Key.ShouldBe("2026-W44");
        DigestWeek.Of(DateOnly.FromDateTime(sunday.UtcDateTime)).Key.ShouldBe("2026-W43");
    }
}
