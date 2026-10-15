using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Heal;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>The back-off of the quiet retries (ADR 0027): 1, 5 and 15 minutes, three retries at most, only for reasons that may pass and only inside the day of the failure. Monday 2026-10-12, India time.</summary>
public sealed class RetryScheduleTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);
    private static readonly Guid Account = Guid.Parse("00000000-0000-7000-8000-000000000001");

    private readonly FixedHealSettings _settings = new();

    private static DateTimeOffset At(int hour, int minute) => new(2026, 10, 12, hour, minute, 0, Ist);

    private static SyncHealth Failing(int attempts, DateTimeOffset at, string reason = "unavailable", int days = 1) =>
        new(Account, SyncHealthKind.Trades, days, reason, at, attempts, null, false, null, 0);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    public void The_nth_retry_waits_one_five_and_fifteen_minutes_after_the_failure_before_it(int failedAttempts, int minutes)
    {
        var failedAt = At(16, 30);

        RetrySchedule.NextAttemptAt(Failing(failedAttempts, failedAt), _settings).ShouldBe(failedAt.AddMinutes(minutes));
    }

    [Fact]
    public void After_three_retries_all_failed_no_other_is_made()
    {
        RetrySchedule.NextAttemptAt(Failing(attempts: 4, At(16, 50)), _settings).ShouldBeNull();
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("rate_limited")]
    [InlineData("interrupted")]
    public void A_reason_that_may_pass_is_retried(string reason)
    {
        RetrySchedule.NextAttemptAt(Failing(1, At(16, 30), reason), _settings).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("history_too_large")]
    [InlineData("unexpected")]
    [InlineData("token_expired")]
    [InlineData("token_refused")]
    [InlineData("not_connected")]
    [InlineData("role_off")]
    public void A_permanent_reason_is_not_retried(string reason)
    {
        RetrySchedule.NextAttemptAt(Failing(1, At(16, 30), reason), _settings).ShouldBeNull();
    }

    [Fact]
    public void A_retry_that_would_fall_on_the_next_day_is_not_made()
    {
        RetrySchedule.NextAttemptAt(Failing(3, At(23, 50)), _settings).ShouldBeNull(); // 23:50 + 15 min is tomorrow
        RetrySchedule.NextAttemptAt(Failing(2, At(23, 50)), _settings).ShouldNotBeNull(); // 23:55 is not
    }

    [Fact]
    public void A_healthy_kind_has_nothing_to_retry()
    {
        RetrySchedule.NextAttemptAt(SyncHealth.None(Account, SyncHealthKind.Trades), _settings).ShouldBeNull();
    }

    [Fact]
    public void A_retry_is_due_when_its_wait_is_over_and_not_before()
    {
        var failed = Failing(1, At(16, 30));

        RetrySchedule.IsDue(failed, _settings, At(16, 30)).ShouldBeFalse();
        RetrySchedule.IsDue(failed, _settings, At(16, 31)).ShouldBeTrue();
    }

    [Fact]
    public void The_waits_are_the_settings_not_constants()
    {
        _settings.RetryBackoff = [TimeSpan.FromMinutes(2)];
        var failedAt = At(10, 0);

        RetrySchedule.NextAttemptAt(Failing(1, failedAt), _settings).ShouldBe(failedAt.AddMinutes(2));
        RetrySchedule.NextAttemptAt(Failing(2, failedAt), _settings).ShouldBeNull(); // one retry only
    }

    [Fact]
    public void No_waits_switch_the_retries_off()
    {
        _settings.RetryBackoff = [];

        RetrySchedule.NextAttemptAt(Failing(1, At(10, 0)), _settings).ShouldBeNull();
    }

    [Fact]
    public void Retries_remain_while_one_is_still_to_come_on_the_day_of_the_failure()
    {
        var failed = Failing(2, At(16, 30));

        RetrySchedule.RetriesRemain(failed, _settings, At(16, 40)).ShouldBeTrue();
        RetrySchedule.RetriesRemain(failed with { Attempts = 4 }, _settings, At(16, 40)).ShouldBeFalse();
        RetrySchedule.RetriesRemain(failed, _settings, At(16, 40).AddDays(1)).ShouldBeFalse(); // the day is over: the next scheduled run is the only retry left
    }
}
