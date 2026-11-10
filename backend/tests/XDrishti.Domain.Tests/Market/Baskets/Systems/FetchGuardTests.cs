using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Domain.Tests.Market.Baskets.Systems;

public sealed class FetchGuardTests
{
    private static readonly FetchLimits Limits = FetchLimits.Default;

    private static FetchFacts Facts(int rows = 50, int unmatched = 0, int current = 50, int added = 0, int removed = 0, bool window = false, bool forced = false, bool header = true) =>
        new(header, rows, 50, 50, unmatched, current, added, removed, window, forced);

    [Fact]
    public void A_clean_first_fetch_passes()
    {
        FetchGuard.Check(Facts(current: 0, added: 50), Limits).Ok.ShouldBeTrue();
    }

    [Fact]
    public void A_wrong_header_is_rejected_first()
    {
        FetchGuard.Check(Facts(header: false, rows: 10), Limits).Reason.ShouldBe("bad_header");
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(49, false)]
    [InlineData(51, false)]
    public void The_row_count_must_be_inside_the_bound(int rows, bool ok)
    {
        FetchGuard.Check(Facts(rows: rows), Limits).Ok.ShouldBe(ok);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void More_than_a_fifth_of_unmatched_rows_is_rejected(int unmatched, bool ok)
    {
        var verdict = FetchGuard.Check(Facts(unmatched: unmatched), Limits);

        (verdict.Ok, verdict.Reason).ShouldBe(ok ? (true, null) : (false, "unmatched"));
    }

    [Theory]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public void Removing_more_than_half_trips_the_wire_inside_a_review_window(int removed, bool ok)
    {
        var verdict = FetchGuard.Check(Facts(added: removed, removed: removed, window: true), Limits);

        (verdict.Ok, verdict.Reason).ShouldBe(ok ? (true, null) : (false, "tripwire"));
    }

    [Fact]
    public void Force_overrides_the_tripwire_only()
    {
        FetchGuard.Check(Facts(added: 30, removed: 30, window: true, forced: true), Limits).Ok.ShouldBeTrue();
        FetchGuard.Check(Facts(added: 30, removed: 30, window: false, forced: true), Limits).Reason.ShouldBe("too_many_changes");
        FetchGuard.Check(Facts(header: false, forced: true), Limits).Reason.ShouldBe("bad_header");
        FetchGuard.Check(Facts(rows: 49, forced: true), Limits).Reason.ShouldBe("row_count");
        FetchGuard.Check(Facts(unmatched: 11, forced: true), Limits).Reason.ShouldBe("unmatched");
    }

    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(4, 4, false)]
    [InlineData(2, 4, false)]
    [InlineData(4, 2, false)]
    public void Outside_a_review_window_the_change_is_bounded_by_max_of_three_and_six_percent(int added, int removed, bool ok)
    {
        var verdict = FetchGuard.Check(Facts(added: added, removed: removed), Limits);

        (verdict.Ok, verdict.Reason).ShouldBe(ok ? (true, null) : (false, "too_many_changes"));
    }

    [Fact]
    public void Inside_a_review_window_the_change_bound_is_lifted()
    {
        FetchGuard.Check(Facts(added: 10, removed: 10, window: true), Limits).Ok.ShouldBeTrue();
    }

    [Theory]
    [InlineData(50, 3)]
    [InlineData(100, 6)]
    [InlineData(500, 30)]
    [InlineData(10, 3)]
    public void The_allowed_change_is_the_floor_or_the_ceiling_of_the_fraction(int current, int allowed)
    {
        FetchGuard.AllowedChange(current, Limits).ShouldBe(allowed);
    }
}
