using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Market.Context;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Tests.Market.Context;

/// <summary>P3-01e criteria 4 to 6 at the level of the use cases: what is computed, what waits, what is written once and what only the rebuild changes.</summary>
public sealed class BreadthHandlersTests
{
    private static readonly DateOnly[] Sessions = WeekdaysFrom(new DateOnly(2026, 2, 2), 30);

    private static readonly DateOnly LastClosed = new(2026, 3, 9);

    private readonly FakeContextSettings _settings = new() { BreadthSmaSessions = 3, BreadthMinVoters = 2, BreadthMinShare = 0.5, BreadthGraceSessions = 3 };

    private readonly FakeBreadthSource _source = new();

    private readonly FakeContextDayStore _store = new();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromMinutes(330)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ComputeBreadthHandler Compute => new(_settings, _source, _store, new FixedCalendar(), _clock, NullLogger<ComputeBreadthHandler>.Instance);

    private RebuildBreadthHandler Rebuild => new(_settings, _source, _store, new FixedCalendar(), NullLogger<RebuildBreadthHandler>.Instance);

    private static DateOnly[] WeekdaysFrom(DateOnly start, int count)
    {
        var list = new List<DateOnly>();
        for (var d = start; list.Count < count; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                list.Add(d);
            }
        }

        return [.. list];
    }

    private static BreadthInput Share(IEnumerable<DateOnly> dates, Func<int, double> close) =>
        new(Guid.NewGuid(), dates.Select((d, i) => (d, c: close(i))).ToDictionary(x => x.d, x => x.c));

    private static IEnumerable<DateOnly> Until(DateOnly last) => Sessions.Where(d => d <= last);

    private void Days(DateOnly from, DateOnly to)
    {
        foreach (var date in Sessions.Where(d => d >= from && d <= to))
        {
            _store.Rows.Add(new ContextDayRecord(date, 22_010, 21_990, 22_000, 15, 375, 375));
        }
    }

    private void ThreeRisingShares() =>
        _source.Data = new BreadthData([.. Enumerable.Range(0, 3).Select(_ => Share(Until(LastClosed), i => 10 + i))], new Dictionary<Guid, IReadOnlySet<DateOnly>>());

    [Fact]
    public async Task Every_stored_day_without_a_breadth_gets_one_in_a_single_read_that_starts_at_the_window_of_the_oldest()
    {
        Days(new DateOnly(2026, 3, 4), LastClosed);
        ThreeRisingShares();

        var result = await Compute.HandleAsync(new ComputeBreadthCommand(), Ct);

        result.Value.ShouldBe(new BreadthComputed(4, 0, 0, null));
        _store.Breadth.Values.ShouldAllBe(b => b.State == BreadthResult.ValueState && b.Value == 100.0 && b.Voters == 3 && b.Universe == 3);
        _source.Reads.ShouldBe([new DateOnly(2026, 3, 2)], "one read, from the first session of the window of 3 that ends on 2026-03-04");
    }

    [Fact]
    public async Task A_second_run_reads_nothing_and_changes_nothing_even_when_bars_were_loaded_since()
    {
        Days(new DateOnly(2026, 3, 4), LastClosed);
        ThreeRisingShares();
        await Compute.HandleAsync(new ComputeBreadthCommand(), Ct);
        var before = _store.Breadth.ToDictionary(kv => kv.Key, kv => kv.Value);
        var writes = _store.BreadthWrites;
        _source.Data = new BreadthData([.. Enumerable.Range(0, 3).Select(_ => Share(Until(LastClosed), i => 100 - i))], new Dictionary<Guid, IReadOnlySet<DateOnly>>());

        var again = await Compute.HandleAsync(new ComputeBreadthCommand(), Ct);

        again.Value.ShouldBe(new BreadthComputed(0, 0, 0, null));
        _store.Breadth.ShouldBe(before);
        _store.BreadthWrites.ShouldBe(writes);
        _source.Reads.Count.ShouldBe(1, "nothing to compute, so the stock bars are not read again");
    }

    [Fact]
    public async Task The_last_closed_session_waits_while_a_share_has_no_bar_on_it_and_is_computed_once_the_day_is_older()
    {
        Days(new DateOnly(2026, 3, 6), LastClosed);
        _source.Data = new BreadthData(
            [Share(Until(LastClosed), i => 10 + i), Share(Until(LastClosed), i => 10 + i), Share(Until(new DateOnly(2026, 3, 6)), i => 10 + i)],
            new Dictionary<Guid, IReadOnlySet<DateOnly>>());

        var first = await Compute.HandleAsync(new ComputeBreadthCommand(), Ct);

        first.Value.Pending.ShouldBe(1);
        _store.Breadth.ContainsKey(LastClosed).ShouldBeFalse("a day that can still gain bars is not written");
        _store.Breadth.ContainsKey(new DateOnly(2026, 3, 6)).ShouldBeTrue();

        _clock.SetUtcNow(new DateTimeOffset(2026, 3, 11, 17, 0, 0, TimeSpan.FromMinutes(330)));
        var later = await Compute.HandleAsync(new ComputeBreadthCommand(), Ct);

        later.Value.Computed.ShouldBe(1);
        _store.Breadth[LastClosed].Voters.ShouldBe(2);
    }

    [Fact]
    public async Task A_late_load_turns_an_insufficient_day_inside_the_grace_into_a_number_and_after_the_grace_it_is_final()
    {
        Days(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 6));
        var thin = new BreadthData([Share(Until(LastClosed), i => 10 + i), Share(Sessions.Where(d => d > new DateOnly(2026, 3, 5)), i => 10 + i)], new Dictionary<Guid, IReadOnlySet<DateOnly>>());
        _source.Data = thin;
        await Compute.HandleAsync(new ComputeBreadthCommand(), Ct);
        _store.Breadth[new DateOnly(2026, 3, 5)].State.ShouldBe(BreadthResult.Insufficient);
        var full = new BreadthData([Share(Until(LastClosed), i => 10 + i), Share(Until(LastClosed), i => 10 + i)], new Dictionary<Guid, IReadOnlySet<DateOnly>>());

        _source.Data = full;
        await Compute.HandleAsync(new ComputeBreadthCommand(), Ct);
        _store.Breadth[new DateOnly(2026, 3, 5)].State.ShouldBe(BreadthResult.ValueState, "2026-03-05 is within 3 sessions of the last closed one");

        // The same on a day outside the grace: 2026-03-02 was insufficient, and stays so.
        var stale = new FakeContextDayStore();
        stale.Rows.Add(new ContextDayRecord(new DateOnly(2026, 3, 4), 22_010, 21_990, 22_000, 15, 375, 375));
        stale.Breadth[new DateOnly(2026, 3, 4)] = new StoredBreadth(new DateOnly(2026, 3, 4), null, 1, 2, 1, BreadthResult.Insufficient);
        _source.Data = full;
        var handler = new ComputeBreadthHandler(_settings, _source, stale, new FixedCalendar(), _clock, NullLogger<ComputeBreadthHandler>.Instance);

        var result = await handler.HandleAsync(new ComputeBreadthCommand(), Ct);

        result.Value.Computed.ShouldBe(0);
        stale.Breadth[new DateOnly(2026, 3, 4)].State.ShouldBe(BreadthResult.Insufficient);
    }

    [Fact]
    public async Task With_no_tracked_share_nothing_is_written_and_the_answer_names_it()
    {
        Days(new DateOnly(2026, 3, 4), LastClosed);

        var result = await Compute.HandleAsync(new ComputeBreadthCommand(), Ct);

        result.Value.ShouldBe(new BreadthComputed(0, 0, 0, "no_shares"));
        _store.Breadth.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_disabled_context_computes_nothing_and_reads_nothing()
    {
        _settings.Enabled = false;
        Days(new DateOnly(2026, 3, 4), LastClosed);

        var result = await Compute.HandleAsync(new ComputeBreadthCommand(), Ct);

        result.Value.Missing.ShouldBe("disabled");
        _source.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_thin_day_is_stored_as_insufficient_with_its_counts_and_no_number()
    {
        _settings.BreadthMinVoters = 5;
        Days(new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 4));
        ThreeRisingShares();

        var result = await Compute.HandleAsync(new ComputeBreadthCommand(), Ct);

        result.Value.ShouldBe(new BreadthComputed(1, 1, 0, null));
        _store.Breadth[new DateOnly(2026, 3, 4)].ShouldBe(new StoredBreadth(new DateOnly(2026, 3, 4), null, 3, 3, 0, BreadthResult.Insufficient));
    }

    [Fact]
    public async Task The_back_fill_of_1100_days_for_214_shares_is_computed_in_a_single_pass_well_inside_three_seconds()
    {
        var sessions = new List<DateOnly>();
        for (var d = new DateOnly(2021, 1, 4); sessions.Count < 1_120; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                sessions.Add(d);
            }
        }

        var random = new Random(3);
        _settings.BreadthSmaSessions = 20;
        _settings.BreadthMinVoters = 50;
        foreach (var date in sessions.Skip(20))
        {
            _store.Rows.Add(new ContextDayRecord(date, 22_010, 21_990, 22_000, 15, 375, 375));
        }

        var clock0 = new FakeTimeProvider(new DateTimeOffset(sessions[^1].AddDays(5).ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromMinutes(330)));
        _source.Data = new BreadthData([.. Enumerable.Range(0, 214).Select(_ => Share(sessions, i => 100 + random.NextDouble() * 50 + i * 0.01))], new Dictionary<Guid, IReadOnlySet<DateOnly>>());
        var handler = new ComputeBreadthHandler(_settings, _source, _store, new FixedCalendar(), clock0, NullLogger<ComputeBreadthHandler>.Instance);
        var clock = Stopwatch.StartNew();

        var result = await handler.HandleAsync(new ComputeBreadthCommand(), Ct);

        clock.Stop();
        result.Value.Computed.ShouldBe(1_100);
        _source.Reads.Count.ShouldBe(1);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
    }

    // Rebuild

    private async Task StoreOldBreadthAsync()
    {
        Days(new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 6));
        ThreeRisingShares();
        await Compute.HandleAsync(new ComputeBreadthCommand(), Ct);
    }

    [Fact]
    public async Task A_rebuild_dry_run_lists_each_changed_day_with_the_old_and_the_new_figure_and_writes_nothing()
    {
        await StoreOldBreadthAsync();
        var writes = _store.BreadthWrites;
        _source.Data = new BreadthData([.. Enumerable.Range(0, 3).Select(_ => Share(Until(LastClosed), i => 100 - i))], new Dictionary<Guid, IReadOnlySet<DateOnly>>());

        var result = await Rebuild.HandleAsync(new RebuildBreadthCommand(new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 6), false), Ct);

        result.Value.Written.ShouldBeFalse();
        result.Value.Changes.Select(c => (c.Date, c.OldValue, c.NewValue)).ShouldBe([
            (new DateOnly(2026, 3, 4), (double?)100.0, (double?)0.0),
            (new DateOnly(2026, 3, 5), (double?)100.0, (double?)0.0),
            (new DateOnly(2026, 3, 6), (double?)100.0, (double?)0.0)]);
        _store.BreadthWrites.ShouldBe(writes);
        _store.Breadth.Values.ShouldAllBe(b => b.Value == 100.0);
    }

    [Fact]
    public async Task A_rebuild_with_write_replaces_the_stored_figures_and_an_unchanged_day_is_not_listed()
    {
        await StoreOldBreadthAsync();
        _source.Data = new BreadthData([.. Enumerable.Range(0, 3).Select(_ => Share(Until(LastClosed), i => 100 - i))], new Dictionary<Guid, IReadOnlySet<DateOnly>>());

        var result = await Rebuild.HandleAsync(new RebuildBreadthCommand(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 6), true), Ct);

        result.Value.Written.ShouldBeTrue();
        result.Value.Changes.Count.ShouldBe(2);
        _store.Breadth[new DateOnly(2026, 3, 5)].Value.ShouldBe(0.0);
        _store.Breadth[new DateOnly(2026, 3, 4)].Value.ShouldBe(100.0, "outside the range");
        var again = await Rebuild.HandleAsync(new RebuildBreadthCommand(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 6), true), Ct);
        again.Value.Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rebuild_of_more_than_400_sessions_or_a_reversed_range_is_refused()
    {
        var tooWide = await Rebuild.HandleAsync(new RebuildBreadthCommand(new DateOnly(2024, 1, 1), new DateOnly(2026, 3, 6), false), Ct);
        var reversed = await Rebuild.HandleAsync(new RebuildBreadthCommand(new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 1), false), Ct);

        tooWide.Error.Code.ShouldBe("breadth.range_too_wide");
        reversed.Error.Code.ShouldBe("breadth.bad_range");
    }
}
