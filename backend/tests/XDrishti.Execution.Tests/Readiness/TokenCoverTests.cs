using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Tests.Readiness;

/// <summary>P12-11 criterion 3: the token must outlive the session and the final reconciliation (16:45), not just the open.</summary>
public sealed class TokenCoverTests
{
    private static readonly TimeOnly Cover = new(16, 45);

    private static TokenCoverResult At(DateTimeOffset expiry, DateTimeOffset now) => TokenCover.Evaluate(expiry, now, Cover, 20);

    [Fact]
    public void Issued_at_17_00_the_day_before_passes_at_09_10_with_7_hours_50_minutes_left()
    {
        var result = At(Ist.At(27, 17, 0), Ist.At(27, 9, 10));

        result.Outcome.ShouldBe(ReadinessOutcome.Pass);
    }

    [Fact]
    public void Issued_at_15_00_the_day_before_is_token_short_and_says_what_to_do()
    {
        var result = At(Ist.At(27, 15, 0), Ist.At(27, 9, 10));

        result.Outcome.ShouldBe(ReadinessOutcome.Fail);
        result.Detail.ShouldBe("token_short");
        result.Text.ShouldBe("The token expires at 15:00 today: log in again between 16:45 yesterday and 09:00 today.");
    }

    [Fact]
    public void The_cut_off_is_inclusive_to_the_second()
    {
        At(Ist.At(27, 16, 45, 0), Ist.At(27, 9, 10)).Outcome.ShouldBe(ReadinessOutcome.Pass);
        At(Ist.At(27, 16, 44, 59), Ist.At(27, 9, 10)).Detail.ShouldBe("token_short");
    }

    [Fact]
    public void Issued_at_08_20_on_the_day_passes_that_day_and_is_expired_the_next_morning()
    {
        At(Ist.At(28, 8, 20), Ist.At(27, 9, 10)).Outcome.ShouldBe(ReadinessOutcome.Pass);
        At(Ist.At(28, 8, 20), Ist.At(28, 9, 10)).Detail.ShouldBe("token_expired");
    }

    [Fact]
    public void An_expiry_19_minutes_away_is_below_the_minimum()
    {
        var result = At(Ist.At(27, 9, 29), Ist.At(27, 9, 10));

        result.Detail.ShouldBe("token_expiring");
        At(Ist.At(27, 9, 30), Ist.At(27, 9, 10)).Detail.ShouldNotBe("token_expiring");
    }

    [Fact]
    public void No_expiry_is_unavailable_never_a_pass()
    {
        TokenCover.Evaluate(null, Ist.At(27, 9, 10), Cover, 20).Outcome.ShouldBe(ReadinessOutcome.Unavailable);
    }

    [Fact]
    public void Covers_answers_for_a_future_session_date_for_the_evening_reminder()
    {
        TokenCover.Covers(Ist.At(28, 16, 45), new DateOnly(2026, 10, 28), Cover).ShouldBeTrue();
        TokenCover.Covers(Ist.At(27, 17, 0), new DateOnly(2026, 10, 28), Cover).ShouldBeFalse();
    }
}
