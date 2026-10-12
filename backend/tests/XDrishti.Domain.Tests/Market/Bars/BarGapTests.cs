using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Tests.Market.Bars;

public sealed class BarGapTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 20, 0, TimeSpan.Zero);

    private static readonly Guid Instrument = Guid.NewGuid();

    private static readonly DateOnly Day = new(2026, 10, 7);

    private static BarGap Open(int found = 200) => BarGap.Detect(Instrument, Day, 375, found, Now);

    [Fact]
    public void A_session_with_fewer_bars_than_expected_is_detected_as_open_with_no_attempts()
    {
        var gap = Open(200);

        (gap.Status, gap.Expected, gap.Found, gap.Attempts, gap.LastAttemptAt, gap.DetectedAt).ShouldBe((GapStatus.Open, 375, 200, 0, null, Now));
        (gap.InstrumentId, gap.Date).ShouldBe((Instrument, Day));
    }

    [Fact]
    public void A_session_that_is_complete_when_detected_is_recorded_as_filled()
    {
        BarGap.Detect(Instrument, Day, 375, 375, Now).Status.ShouldBe(GapStatus.Filled);
    }

    [Theory]
    [InlineData(375, 375, true)]
    [InlineData(375, 376, true)]
    [InlineData(375, 374, false)]
    [InlineData(60, 60, true)]
    [InlineData(0, 0, true)]
    public void A_session_is_complete_when_it_has_at_least_the_expected_bars(int expected, int found, bool complete)
    {
        BarGap.IsComplete(expected, found).ShouldBe(complete);
    }

    [Fact]
    public void A_refetch_that_completes_the_session_fills_the_gap_and_counts_the_attempt()
    {
        var gap = Open(200);

        gap.RecordRefetch(200, 375, Now.AddMinutes(5));

        (gap.Status, gap.Found, gap.Attempts, gap.LastAttemptAt).ShouldBe((GapStatus.Filled, 375, 1, Now.AddMinutes(5)));
    }

    [Fact]
    public void A_refetch_that_adds_some_bars_but_not_all_leaves_the_gap_open_with_the_new_count()
    {
        var gap = Open(200);

        gap.RecordRefetch(200, 300, Now.AddMinutes(5));

        (gap.Status, gap.Found, gap.Attempts).ShouldBe((GapStatus.Open, 300, 1));
    }

    [Fact]
    public void A_refetch_that_adds_nothing_makes_the_gap_unfillable()
    {
        var gap = Open(200);

        gap.RecordRefetch(200, 200, Now.AddMinutes(5));

        (gap.Status, gap.Found, gap.Attempts).ShouldBe((GapStatus.Unfillable, 200, 1));
    }

    [Fact]
    public void An_unfillable_gap_that_a_later_refetch_completes_is_filled()
    {
        var gap = Open(200);
        gap.RecordRefetch(200, 200, Now);

        gap.RecordRefetch(200, 375, Now.AddDays(1));

        (gap.Status, gap.Attempts).ShouldBe((GapStatus.Filled, 2));
    }

    [Fact]
    public void Refreshing_with_a_complete_count_fills_the_gap_without_an_attempt()
    {
        var gap = Open(200);

        gap.Refresh(375, 375, Now.AddMinutes(1)).ShouldBeTrue();

        (gap.Status, gap.Attempts, gap.LastAttemptAt).ShouldBe((GapStatus.Filled, 0, null));
    }

    [Fact]
    public void Refreshing_an_unfillable_gap_with_a_new_incomplete_count_keeps_it_unfillable()
    {
        var gap = Open(200);
        gap.RecordRefetch(200, 200, Now);

        gap.Refresh(375, 250, Now.AddDays(1)).ShouldBeTrue();

        (gap.Status, gap.Found).ShouldBe((GapStatus.Unfillable, 250));
    }

    [Fact]
    public void A_filled_gap_that_loses_bars_opens_again_as_a_new_detection()
    {
        var gap = Open(200);
        gap.RecordRefetch(200, 375, Now);

        gap.Refresh(375, 300, Now.AddDays(3)).ShouldBeTrue();

        (gap.Status, gap.Found, gap.Attempts, gap.LastAttemptAt, gap.DetectedAt).ShouldBe((GapStatus.Open, 300, 0, null, Now.AddDays(3)));
    }

    [Fact]
    public void Refreshing_with_nothing_changed_reports_no_change()
    {
        var gap = Open(200);

        gap.Refresh(375, 200, Now.AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public void A_session_that_stopped_being_a_session_counts_as_complete()
    {
        var gap = Open(200);

        gap.Refresh(0, 200, Now).ShouldBeTrue(); // the calendar made the date a holiday

        gap.Status.ShouldBe(GapStatus.Filled);
    }

    [Fact]
    public void Negative_counts_are_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => BarGap.Detect(Instrument, Day, -1, 0, Now));
        Should.Throw<ArgumentOutOfRangeException>(() => Open().RecordRefetch(0, -5, Now));
    }

    [Theory]
    [InlineData("open", GapStatus.Open)]
    [InlineData("filled", GapStatus.Filled)]
    [InlineData("unfillable", GapStatus.Unfillable)]
    public void Status_codes_round_trip(string code, GapStatus status)
    {
        GapStatusCodes.TryParse(code, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(status);
        status.ToCode().ShouldBe(code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("OPEN")]
    [InlineData("done")]
    [InlineData(null)]
    public void Unknown_status_codes_are_not_parsed(string? code)
    {
        GapStatusCodes.TryParse(code, out _).ShouldBeFalse();
    }
}
