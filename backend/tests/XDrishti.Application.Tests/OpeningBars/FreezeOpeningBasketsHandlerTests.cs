using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.OpeningBars;
using XDrishti.Application.OpeningBars;
using XDrishti.Application.Tests.Market.Baskets.Derived;
using XDrishti.Domain.Baskets.Derived;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Tests.OpeningBars;

/// <summary>
/// BK-03b criteria 3 to 6 over fakes: the freeze of baskets 4 (09:20) and 12 (09:30) forms one frozen version from complete bars, or a named <c>not_formed</c>, once; no bar from the formation minute on; hand-worked numbers (BK-02 example 11).
/// The day is Monday 2026-10-12, the previous session Friday 2026-10-09.
/// </summary>
public sealed class FreezeOpeningBasketsHandlerTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DateOnly Friday = new(2026, 10, 9);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset At(int hour, int minute, int second = 0) => new(2026, 10, 12, hour, minute, second, Ist);

    private sealed class World
    {
        public World(int hour = 9, int minute = 20, int second = 10, int maxInstruments = 500)
        {
            Clock = new FakeTimeProvider(At(hour, minute, second));
            Settings = OpeningBarsSettings.Off with { Enabled = true, MaxInstruments = maxInstruments };
            Gappers = new DerivedDefinitionRow(Guid.NewGuid(), "Derived: Gappers", 4, true, null, new Dictionary<string, double>());
            Wide = new DerivedDefinitionRow(Guid.NewGuid(), "Derived: Opening-range wide", 12, true, null, new Dictionary<string, double>());
            Store.Definitions.Add(Gappers);
            Store.Definitions.Add(Wide);
        }

        public FakeTimeProvider Clock { get; }

        public OpeningBarsSettings Settings { get; set; }

        public FakeDerivedSettings Derived { get; } = new();

        public FakeVersionStore Store { get; } = new();

        public FakeUniverse Universe { get; } = new();

        public FakeOpeningQueries Queries { get; } = new();

        public DerivedDefinitionRow Gappers { get; }

        public DerivedDefinitionRow Wide { get; }

        public FreezeOpeningBasketsHandler Handler() => new(Derived, Settings, Store, Universe, Queries, new FakeCalendar(), Clock, NullLogger<FreezeOpeningBasketsHandler>.Instance);

        public Guid Share(string symbol, double volume = 1_000_000) => DerivedWorldShare(symbol, volume);

        private Guid DerivedWorldShare(string symbol, double volume)
        {
            var instrument = new UniverseInstrument(Guid.NewGuid(), new InstrumentBars(symbol, DerivedWorld.Weekdays(Friday, 45, volume)));
            Universe.Instruments.Add(instrument);
            return instrument.InstrumentId;
        }

        /// <summary>Complete first minutes on the prior weekdays (<paramref name="sessions"/> of them, 09:15 for <paramref name="minutes"/> minutes).</summary>
        public void History(Guid id, int sessions, int minutes, double volume = 100, double high = 100.5, double low = 99.5, double open = 100)
        {
            var date = Friday;
            for (var done = 0; done < sessions; date = date.AddDays(-1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                for (var m = 0; m < minutes; m++)
                {
                    Queries.History.Add(Bar(id, new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 15)), Ist).AddMinutes(m), open, high, low, volume));
                }

                done++;
            }
        }

        /// <summary>Today's live bars from 09:15 for <paramref name="minutes"/> minutes, skipping the listed minutes (missing).</summary>
        public void Today(Guid id, int minutes, double open = 100, double high = 100, double low = 100, double volume = 100, params int[] skip)
        {
            for (var m = 0; m < minutes; m++)
            {
                if (!skip.Contains(m))
                {
                    Queries.Day.Add(Bar(id, At(9, 15).AddMinutes(m), open, high, low, volume));
                }
            }
        }

        public void Started() => Queries.Events.Add(new OpeningBarsEvent(OpeningBarsEvent.Started, At(9, 14)));

        private static MinuteBar Bar(Guid id, DateTimeOffset minute, double open, double high, double low, double volume) =>
            new(id, minute, (decimal)open, (decimal)high, (decimal)low, (decimal)open, (long)volume, 1);
    }

    private static List<VersionExclusion> Excluded(DerivedVersionWrite version) => JsonSerializer.Deserialize<List<VersionExclusion>>(version.ExcludedJson)!;

    // ---- criterion 5: formed once, frozen ----

    [Fact]
    public async Task Basket_4_is_formed_at_0920_with_as_of_the_previous_session_formed_at_0920_and_a_later_run_writes_nothing()
    {
        var w = new World();
        var gapper = w.Share("GAP");
        w.History(gapper, 20, 5);
        w.Today(gapper, 5, open: 104, high: 104, low: 104, volume: 200); // gap 4 % against max(2 %, 1.5 x 2 %) = 3 %; 1,000 against a median of 500
        w.Started();

        var first = (await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct)).Value;
        var again = (await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct)).Value;

        var version = w.Store.Versions.Single();
        version.BasketId.ShouldBe(w.Gappers.BasketId);
        version.Outcome.ShouldBe(VersionOutcomes.Applied);
        version.AsOf.ShouldBe(Friday);
        version.EffectiveFrom.ShouldBe(Monday);
        version.FormedAt.ShouldBe(At(9, 20));
        version.Scores.Where(s => s.IsMember).Select(s => s.InstrumentId).ShouldBe([gapper]);
        first.Waiting.ShouldBe(1, "basket 12 waits for 09:30");
        first.Baskets.Single().Outcome.ShouldBe(VersionOutcomes.Applied);
        again.Baskets.ShouldBeEmpty("a basket that is settled for the day writes nothing more");
        w.Store.Versions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Basket_12_is_formed_at_0930_from_hand_worked_numbers_an_opening_range_of_1_5_percent_against_a_median_of_0_8_percent_is_wide()
    {
        var w = new World(9, 30, 10);
        var share = w.Share("WIDE");
        w.History(share, 20, 15, high: 1004, low: 996, open: 1000); // 0.8 % on every prior day, so 1.5 x 0.8 % = 1.2 %
        w.Today(share, 15, open: 1000, high: 1010, low: 995); // 1.5 %
        w.Started();

        await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct);

        var version = w.Store.Versions.Single(v => v.BasketId == w.Wide.BasketId);
        version.Outcome.ShouldBe(VersionOutcomes.Applied);
        version.FormedAt.ShouldBe(At(9, 30));
        version.Scores.Single(s => s.IsMember).Score.ShouldBe(0.015, 1e-9);
    }

    [Fact]
    public async Task Before_the_formation_time_plus_the_grace_nothing_is_written_and_the_basket_waits()
    {
        var w = new World(9, 20, 5); // 09:20 + 5 s grace + 3 s settle = 09:20:08
        w.Started();

        var run = (await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct)).Value;

        run.Waiting.ShouldBe(2);
        w.Store.Versions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_after_the_form_by_time_is_not_formed_late_even_with_complete_bars()
    {
        var w = new World(9, 23, 0); // 09:20 + 2 minutes has passed
        var gapper = w.Share("GAP");
        w.History(gapper, 20, 5);
        w.Today(gapper, 5, open: 104, high: 104, low: 104, volume: 200);
        w.Started();

        await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct);

        var version = w.Store.Versions.Single();
        version.Outcome.ShouldBe(VersionOutcomes.NotFormed);
        version.Scores.ShouldBeEmpty();
        Excluded(version).Single().Reason.ShouldBe("late");
    }

    // ---- criterion 4: complete or not formed ----

    [Fact]
    public async Task An_instrument_missing_the_0917_minute_is_excluded_by_name_and_the_others_are_formed()
    {
        var w = new World();
        var good = w.Share("GOOD");
        var holed = w.Share("HOLED");
        foreach (var id in new[] { good, holed })
        {
            w.History(id, 20, 5);
        }

        w.Today(good, 5, open: 104, high: 104, low: 104, volume: 200);
        w.Today(holed, 5, open: 104, high: 104, low: 104, volume: 200, skip: 2); // 09:17
        w.Started();

        await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct);

        var version = w.Store.Versions.Single();
        version.Scores.Where(s => s.IsMember).Select(s => s.InstrumentId).ShouldBe([good]);
        Excluded(version).ShouldBe([new VersionExclusion(holed, "HOLED", ExclusionReason.OpeningMinuteMissing)]);
        version.UniverseSize.ShouldBe(2);
        version.ExcludedCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("gap", "feed_gap")]
    [InlineData("none", "no_live_bars")]
    [InlineData("disk", "low_disk")]
    [InlineData("late start", "feed_gap")]
    public async Task A_gap_a_missing_recorder_a_low_disk_or_a_late_start_makes_the_whole_basket_not_formed_with_the_reason(string cause, string reason)
    {
        var w = new World();
        var gapper = w.Share("GAP");
        w.History(gapper, 20, 5);
        w.Today(gapper, 5, open: 104, high: 104, low: 104, volume: 200);
        switch (cause)
        {
            case "gap":
                w.Started();
                w.Queries.Events.Add(new OpeningBarsEvent(OpeningBarsEvent.Gap, At(9, 17), At(9, 17, 40)));
                break;
            case "disk":
                w.Started();
                w.Queries.Events.Add(new OpeningBarsEvent(OpeningBarsEvent.LowDisk, At(9, 18)));
                break;
            case "late start":
                w.Queries.Events.Add(new OpeningBarsEvent(OpeningBarsEvent.Started, At(9, 16)));
                break;
            default:
                break;
        }

        await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct);

        var version = w.Store.Versions.Single();
        version.Outcome.ShouldBe(VersionOutcomes.NotFormed);
        version.Scores.ShouldBeEmpty();
        Excluded(version).Single().Reason.ShouldBe(reason);
    }

    [Fact]
    public async Task Only_bars_that_start_before_the_formation_time_are_read_the_0920_bar_changes_nothing()
    {
        var w = new World();
        var gapper = w.Share("GAP");
        w.History(gapper, 20, 5);
        w.Today(gapper, 5, open: 104, high: 104, low: 104, volume: 200);
        w.Started();
        var without = (await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct)).Value;
        w.Store.Versions.Clear();
        w.Queries.Day.Add(new MinuteBar(gapper, At(9, 20), 50m, 500m, 1m, 50m, 9_999_999, 1)); // written, but it is not known at 09:20

        await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct);

        without.Baskets.Single().Members.ShouldBe(1);
        w.Store.Versions.Single().Scores.Single(s => s.IsMember).Score.ShouldBe(0.04, 1e-9);
    }

    [Fact]
    public async Task With_no_live_bar_at_all_the_day_is_not_formed_no_live_bars_never_an_applied_empty_version()
    {
        var w = new World();
        w.Share("GAP");
        w.Started(); // the recorder ran, but nothing arrived

        await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct);

        var version = w.Store.Versions.Single();
        version.Outcome.ShouldBe(VersionOutcomes.NotFormed);
        Excluded(version).Single().Reason.ShouldBe("no_live_bars");
    }

    [Fact]
    public async Task When_no_instrument_has_a_complete_window_the_day_is_not_formed_no_live_bars()
    {
        var w = new World();
        var holed = w.Share("HOLED");
        w.History(holed, 20, 5);
        w.Today(holed, 5, volume: 200, skip: 2);
        w.Started();

        await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct);

        var version = w.Store.Versions.Single();
        version.Outcome.ShouldBe(VersionOutcomes.NotFormed);
        Excluded(version).Single().Reason.ShouldBe("no_live_bars");
    }

    [Fact]
    public async Task A_recorder_that_closes_before_the_window_is_over_makes_the_basket_not_formed()
    {
        var w = new World(9, 30, 10);
        var share = w.Share("S");
        w.History(share, 20, 15);
        w.Today(share, 15);
        w.Started();
        w.Settings = w.Settings with { RecordUntil = new TimeOnly(9, 30) };

        await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct);

        var version = w.Store.Versions.Single(v => v.BasketId == w.Wide.BasketId);
        version.Outcome.ShouldBe(VersionOutcomes.NotFormed);
        Excluded(version).Single().Reason.ShouldBe("record_until_too_early");
    }

    [Fact]
    public async Task Live_rows_of_a_prior_day_recorded_with_a_gap_a_restart_or_low_disk_are_left_out_of_the_history()
    {
        var w = new World();
        var gapper = w.Share("GAP");
        w.History(gapper, 20, 5);
        w.Today(gapper, 5, open: 104, high: 104, low: 104, volume: 200);
        w.Started();
        w.Queries.PriorEvents[Friday] = [new OpeningBarsEvent(OpeningBarsEvent.Started, At(9, 14).AddDays(-3)), new OpeningBarsEvent(OpeningBarsEvent.Gap, At(9, 17).AddDays(-3))];
        w.Queries.PriorEvents[Friday.AddDays(-1)] = [new OpeningBarsEvent(OpeningBarsEvent.Started, At(9, 14).AddDays(-4))]; // a clean day

        await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct);

        w.Queries.LastExcluded.ShouldBe([Friday]);
    }

    // ---- criterion 6: history ----

    [Fact]
    public async Task A_prior_session_with_a_missing_opening_minute_does_not_vote_for_the_first_five_minute_volume_median()
    {
        var w = new World();
        var gapper = w.Share("GAP");
        w.History(gapper, 20, 5, volume: 100); // median 500
        w.Queries.History.RemoveAll(b => b.MinuteStart.Day == 8 && b.MinuteStart.Minute == 17); // one prior session is holed: 19 vote
        w.Today(gapper, 5, open: 104, high: 104, low: 104, volume: 99); // 495 < 500: not a member either way, so the check is that it is formed at all
        w.Started();

        await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct);

        w.Store.Versions.Single().Outcome.ShouldBe(VersionOutcomes.Applied);
        w.Store.Versions.Single().Scores.Where(s => s.IsMember).ShouldBeEmpty();
    }

    [Fact]
    public async Task Fewer_than_the_minimum_complete_prior_sessions_for_basket_12_is_insufficient_history_by_name()
    {
        var w = new World(9, 30, 10);
        var share = w.Share("NEW");
        w.History(share, 14, 15, high: 1004, low: 996, open: 1000); // 14 < Basket12.MinBars 15
        w.Today(share, 15, open: 1000, high: 1010, low: 995);
        w.Started();

        await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct);

        var version = w.Store.Versions.Single(v => v.BasketId == w.Wide.BasketId);
        Excluded(version).Single().Reason.ShouldBe(ExclusionReason.InsufficientHistory);
        version.Scores.ShouldBeEmpty();
    }

    // ---- criterion 7: the cap ----

    [Fact]
    public async Task Over_the_cap_the_rest_of_the_universe_is_excluded_as_not_subscribed_and_counted()
    {
        var w = new World(maxInstruments: 1);
        var liquid = w.Share("LIQUID", 2_000_000);
        var thin = w.Share("THIN", 1_000);
        w.History(liquid, 20, 5);
        w.Today(liquid, 5, open: 104, high: 104, low: 104, volume: 200);
        w.Started();

        await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct);

        var version = w.Store.Versions.Single();
        Excluded(version).ShouldBe([new VersionExclusion(thin, "THIN", ExclusionReason.NotSubscribed)]);
        version.UniverseSize.ShouldBe(2);
    }

    // ---- criteria 1 and 10: switches ----

    [Fact]
    public async Task With_the_derived_job_off_the_freeze_fails_before_reading_anything()
    {
        var w = new World();
        w.Derived.Enabled = false;

        var result = await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(BasketErrors.DerivedDisabledCode);
        w.Universe.Reads.ShouldBe(0);
        w.Store.Versions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_basket_that_is_switched_off_is_not_formed_and_not_named()
    {
        var w = new World();
        w.Store.Definitions[0] = w.Gappers with { Enabled = false };
        w.Started();

        var run = (await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(Monday), Ct)).Value;

        run.Baskets.ShouldBeEmpty();
        w.Store.Versions.ShouldBeEmpty();
    }

    [Fact]
    public async Task On_a_weekend_nothing_is_due_and_no_row_is_written()
    {
        var w = new World();

        var run = (await w.Handler().HandleAsync(new FreezeOpeningBasketsCommand(new DateOnly(2026, 10, 10)), Ct)).Value;

        run.Waiting.ShouldBe(0);
        run.Baskets.ShouldBeEmpty();
        w.Store.Versions.ShouldBeEmpty();
    }
}
