using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

public sealed class BroadcastRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_offset_time_is_stored_as_the_utc_instant()
    {
        var time = BroadcastRule.Evaluate("2026-07-24T16:12:00+05:30", Now);

        time.State.ShouldBe(BroadcastState.Ok);
        time.Instant.ShouldBe(new DateTimeOffset(2026, 7, 24, 10, 42, 0, TimeSpan.Zero));
        time.Reason.ShouldBeNull();
    }

    [Fact]
    public void A_zulu_time_is_taken_as_utc()
    {
        BroadcastRule.Evaluate("2026-07-24T16:12:00Z", Now).Instant.ShouldBe(new DateTimeOffset(2026, 7, 24, 16, 12, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("2026-07-24", "date_only")]
    [InlineData("2026-07-24T16:12:00", "no_offset")]
    [InlineData("2026-07-24T16:12", "no_offset")]
    [InlineData(null, "missing")]
    [InlineData("", "missing")]
    [InlineData("  ", "missing")]
    [InlineData("soon", "unparsable")]
    [InlineData("2026-13-45T10:00:00Z", "unparsable")]
    [InlineData("2026-07-24 16:12:00+05:30", "unparsable")]
    [InlineData("1999-12-31T23:59:00Z", "before_2000")]
    public void A_time_that_cannot_be_trusted_is_unavailable_with_a_reason_and_no_instant(string? raw, string reason)
    {
        var time = BroadcastRule.Evaluate(raw, Now);

        time.State.ShouldBe(BroadcastState.Unavailable);
        time.Reason.ShouldBe(reason);
        time.Instant.ShouldBeNull();
    }

    [Fact]
    public void A_time_ten_minutes_ahead_of_the_clock_is_unavailable_but_five_minutes_is_allowed()
    {
        BroadcastRule.Evaluate("2026-11-02T12:10:00Z", Now).Reason.ShouldBe("broadcast_in_future");
        BroadcastRule.Evaluate("2026-11-02T12:05:00Z", Now).State.ShouldBe(BroadcastState.Ok);
    }
}
