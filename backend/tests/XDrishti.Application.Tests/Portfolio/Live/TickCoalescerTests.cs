using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Portfolio.Live;

namespace XDrishti.Application.Tests.Portfolio.Live;

/// <summary>The in-memory coalescing of ticks between two flushes (ADR 0024 §3): the latest per instrument by tick time, older ones dropped, nothing pending after a drain.</summary>
public sealed class TickCoalescerTests
{
    private static readonly DateTimeOffset T = new(2026, 10, 12, 4, 30, 0, TimeSpan.Zero);
    private static readonly Guid A = Guid.Parse("00000000-0000-7000-8000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-7000-8000-00000000000b");

    private readonly TickCoalescer _coalescer = new();

    private static LivePrice Tick(Guid id, decimal price, int seconds) => new(id, price, T.AddSeconds(seconds));

    [Fact]
    public void A_new_coalescer_has_no_changes_and_drains_nothing()
    {
        _coalescer.HasChanges.ShouldBeFalse();
        _coalescer.Drain().ShouldBeEmpty();
    }

    [Fact]
    public void The_latest_tick_of_an_instrument_wins()
    {
        _coalescer.Add(Tick(A, 100m, 1)).ShouldBeTrue();
        _coalescer.Add(Tick(A, 101m, 3)).ShouldBeTrue();
        _coalescer.Add(Tick(A, 102m, 2)).ShouldBeFalse(); // arrived late

        _coalescer.Drain().ShouldBe([Tick(A, 101m, 3)]);
    }

    [Fact]
    public void An_equal_tick_time_with_an_equal_price_is_a_duplicate_and_dropped()
    {
        _coalescer.Add(Tick(A, 100m, 1));

        _coalescer.Add(Tick(A, 100m, 1)).ShouldBeFalse();

        _coalescer.Drain().Single().Price.ShouldBe(100m);
    }

    [Fact]
    public void An_equal_tick_time_with_another_price_is_a_new_tick_because_trade_times_are_whole_seconds()
    {
        _coalescer.Add(Tick(A, 100m, 1));

        _coalescer.Add(Tick(A, 105m, 1)).ShouldBeTrue();

        _coalescer.Drain().Single().Price.ShouldBe(105m);
        _coalescer.Add(Tick(A, 105m, 1)).ShouldBeFalse(); // the same again, after the flush
        _coalescer.Add(Tick(A, 99m, 0)).ShouldBeFalse(); // older
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_price_that_is_not_positive_is_dropped(double price)
    {
        _coalescer.Add(Tick(A, (decimal)price, 1)).ShouldBeFalse();

        _coalescer.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public void Each_instrument_keeps_its_own_latest_and_the_drain_is_ordered_by_instrument()
    {
        _coalescer.Add(Tick(B, 20m, 5));
        _coalescer.Add(Tick(A, 10m, 1));

        _coalescer.Drain().Select(t => t.InstrumentId).ShouldBe([A, B]);
    }

    [Fact]
    public void A_drain_empties_the_pending_set_and_a_tick_not_newer_than_the_flushed_one_is_still_dropped()
    {
        _coalescer.Add(Tick(A, 100m, 5));
        _coalescer.HasChanges.ShouldBeTrue();
        _coalescer.Drain().Count.ShouldBe(1);

        _coalescer.HasChanges.ShouldBeFalse();
        _coalescer.Add(Tick(A, 99m, 4)).ShouldBeFalse();
        _coalescer.Add(Tick(A, 100m, 5)).ShouldBeFalse();
        _coalescer.Drain().ShouldBeEmpty();
        _coalescer.Add(Tick(A, 100m, 6)).ShouldBeTrue(); // the same price at a newer time is a change (a new trade)
    }

    [Fact]
    public void Requeue_puts_a_failed_batch_back_but_keeps_a_newer_tick_that_arrived_meanwhile()
    {
        _coalescer.Add(Tick(A, 100m, 1));
        _coalescer.Add(Tick(B, 20m, 1));
        var batch = _coalescer.Drain();
        _coalescer.Add(Tick(A, 101m, 2));

        _coalescer.Requeue(batch);

        _coalescer.Drain().ShouldBe([Tick(A, 101m, 2), Tick(B, 20m, 1)]);
    }

    [Fact]
    public void Reset_forgets_pending_ticks_and_the_newest_times()
    {
        _coalescer.Add(Tick(A, 100m, 9));

        _coalescer.Reset();

        _coalescer.HasChanges.ShouldBeFalse();
        _coalescer.Add(Tick(A, 90m, 1)).ShouldBeTrue();
    }
}
