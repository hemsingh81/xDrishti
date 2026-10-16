namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>The books the fake clock keeps of its timers: the test world moves the clock from wake-up to wake-up by them (ISS-14).</summary>
public sealed class SettableClockTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 4, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Without_a_timer_there_is_no_next_wake_up()
    {
        var clock = new SettableClock(Start);

        clock.NextTimerDue.ShouldBeNull();
        clock.TimersCreated.ShouldBe(0);
        clock.TimersFired.ShouldBe(0);
    }

    [Fact]
    public void A_one_shot_timer_is_due_at_its_time_and_gone_once_it_has_fired()
    {
        var clock = new SettableClock(Start);
        using var timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        clock.NextTimerDue.ShouldBe(Start.AddSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));

        clock.TimersFired.ShouldBe(1);
        clock.NextTimerDue.ShouldBeNull();
    }

    [Fact]
    public void A_periodic_timer_is_due_again_a_period_after_each_firing()
    {
        var clock = new SettableClock(Start);
        using var timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        clock.Advance(TimeSpan.FromSeconds(2));
        clock.NextTimerDue.ShouldBe(Start.AddSeconds(4));
        clock.Advance(TimeSpan.FromSeconds(6)); // three periods pass in one move: three callbacks

        clock.TimersFired.ShouldBe(4);
        clock.NextTimerDue.ShouldBe(Start.AddSeconds(10));
    }

    [Fact]
    public void The_next_wake_up_is_the_earliest_of_the_timers_that_are_alive()
    {
        var clock = new SettableClock(Start);
        using var later = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        var sooner = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        clock.NextTimerDue.ShouldBe(Start.AddSeconds(1));
        sooner.Dispose();

        clock.NextTimerDue.ShouldBe(Start.AddSeconds(30));
        clock.TimersCreated.ShouldBe(2);
    }

    [Fact]
    public async Task A_disposed_timer_never_fires_and_is_no_wake_up()
    {
        var clock = new SettableClock(Start);
        var timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        await timer.DisposeAsync();
        clock.Advance(TimeSpan.FromSeconds(10));

        clock.TimersFired.ShouldBe(0);
        clock.NextTimerDue.ShouldBeNull();
    }

    [Fact]
    public void The_callback_still_runs_and_receives_its_state()
    {
        var clock = new SettableClock(Start);
        var received = new List<object?>();
        using var timer = clock.CreateTimer(received.Add, "state", TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(1));

        received.ShouldBe(["state"]);
    }

    [Fact]
    public void A_timer_that_is_changed_is_due_at_its_new_time()
    {
        var clock = new SettableClock(Start);
        using var timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);

        timer.Change(TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan).ShouldBeTrue();

        clock.NextTimerDue.ShouldBe(Start.AddSeconds(3));
    }

    [Fact]
    public async Task Whoever_hears_of_a_wait_finds_the_timer_in_place()
    {
        var clock = new SettableClock(Start);
        var fired = 0;
        var waiting = clock.WhenWaitingForAsync(TimeSpan.FromSeconds(4));

        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
        await waiting;
        clock.Advance(TimeSpan.FromSeconds(4));

        fired.ShouldBe(1);
    }

    [Fact]
    public void The_waits_are_a_copy_that_the_code_under_test_cannot_change_while_it_is_read()
    {
        var clock = new SettableClock(Start);
        using var first = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        var seen = clock.Waits;

        using var second = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        seen.ShouldBe([TimeSpan.FromSeconds(1)]);
        clock.Waits.ShouldBe([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);
    }

    [Fact]
    public void A_timer_is_due_by_its_time_until_its_callback_has_run()
    {
        var clock = new SettableClock(Start);
        using var timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        clock.HasTimerDueBy(Start.AddSeconds(4)).ShouldBeFalse();
        clock.HasTimerDueBy(Start.AddSeconds(5)).ShouldBeTrue();
        clock.Advance(TimeSpan.FromSeconds(5)); // nobody else reads this clock, so the callback runs inside Advance

        clock.HasTimerDueBy(Start.AddSeconds(5)).ShouldBeFalse();
    }

    [Fact]
    public void A_disposed_timer_is_never_due()
    {
        var clock = new SettableClock(Start);
        var timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        timer.Dispose();

        clock.HasTimerDueBy(Start.AddHours(1)).ShouldBeFalse();
    }

    [Fact]
    public void A_timer_that_is_changed_after_it_was_disposed_stays_gone()
    {
        var clock = new SettableClock(Start);
        var timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        timer.Dispose();

        timer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        clock.NextTimerDue.ShouldBeNull();
        clock.HasTimerDueBy(Start.AddHours(1)).ShouldBeFalse();
    }

    [Fact]
    public void A_one_shot_timer_that_fired_and_is_changed_is_due_again()
    {
        var clock = new SettableClock(Start);
        var timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        clock.Advance(TimeSpan.FromSeconds(1));
        clock.NextTimerDue.ShouldBeNull();

        timer.Change(TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);

        clock.NextTimerDue.ShouldBe(Start.AddSeconds(4));
        clock.HasTimerDueBy(Start.AddSeconds(4)).ShouldBeTrue();
    }

    [Fact]
    public void MoveTo_goes_to_the_instant_and_does_nothing_when_the_clock_is_already_there()
    {
        var clock = new SettableClock(Start);

        clock.MoveTo(Start.AddSeconds(5));
        clock.MoveTo(Start.AddSeconds(2));

        clock.GetUtcNow().ShouldBe(Start.AddSeconds(5));
    }
}
