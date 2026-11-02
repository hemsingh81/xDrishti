using XDrishti.Domain.Filings;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>
/// P15-06 criteria 2 and 4 through the replay and the run assembly: the counters of the filter in the record of the run (<c>event_filter_blocked</c> and <c>event_filter_unavailable</c> are the fates; <c>calendar_rows</c>, <c>calendar_sha256</c>, <c>evf_version</c> the event-filter record),
/// the skip of an instrument handed no snapshot, a strategy without the filter unchanged by a snapshot, and a run key that changes with the strategy, the snapshot hash and the lag.
/// </summary>
public sealed class EventFilterReplayTests
{
    private static readonly PreparedStrategy Filtered = Rep.Prepare(TestStrategies.OrbBuyOnly() with { Filters = ["not_event_day"] });

    private static readonly PreparedStrategy Plain = Rep.Prepare(TestStrategies.OrbBuyOnly());

    private static InstrumentReplay Replay(PreparedStrategy strategy, EventCalendarSnapshot? events) =>
        InstrumentReplayer.Replay(strategy, Rep.Input(Cases.F("F1").Bars, Mon, Mon) with { Events = events });

    private static string Key(PreparedStrategy strategy, EventCalendarSnapshot? events)
    {
        var replay = Replay(strategy, events);
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(Rep.Id, "TEST")]);
        var record = events is null ? null : new EventFilterRecord(events.Sha256, events.RowCount, events.LagMinutes, EventCalendarSnapshot.RuleVersion);
        var header = new RunHeader(strategy.Record, SimulationEngine.Version, Mon, Mon, ReplayOptions.Default, false, strategy.Fills.ToCanonicalJson(), universe, DefaultWeek(), Events: record);
        return RunAssembler.Assemble(header, [replay]).RunKey;
    }

    [Fact]
    public void A_blocked_entry_is_a_fate_of_the_record_and_the_identity_of_the_fates_holds()
    {
        var replay = Replay(Filtered, EventRows.Snapshot(Rep.Id, Mon));

        ReplayIdentities.ShouldHold(replay);
        replay.Record.Fates.EventBlocked.ShouldBe(1);
        replay.Record.Fates.EventUnavailable.ShouldBe(0);
        replay.Record.Fates.Total.ShouldBe(replay.Record.Scans.Sum(s => s.Candidates));
        replay.Record.Walk.EventBlocked.ShouldBe(1);
        replay.Trades.ShouldBeEmpty();
        replay.Record.Skipped.ShouldBeNull();
    }

    [Fact]
    public void An_unavailable_entry_is_its_own_fate_and_is_not_counted_as_blocked_or_taken()
    {
        var replay = Replay(Filtered, EventCalendarSnapshot.Create([], 5));

        ReplayIdentities.ShouldHold(replay);
        (replay.Record.Fates.EventUnavailable, replay.Record.Fates.EventBlocked, replay.Record.Fates.Trades).ShouldBe((1L, 0L, 0L));
        replay.Trades.ShouldBeEmpty();
    }

    [Fact]
    public void An_instrument_handed_no_snapshot_for_a_strategy_that_needs_one_is_skipped_with_a_reason_and_no_trade()
    {
        var replay = Replay(Filtered, null);

        replay.Record.Skipped.ShouldBe(SkipReason.EventCalendarUnavailable);
        replay.Record.SkipDetail.ShouldBe("no_event_calendar");
        replay.Trades.ShouldBeEmpty();
        replay.Record.Scans.ShouldBeEmpty();
        SkipReason.EventCalendarUnavailable.ToCode().ShouldBe("event_calendar_unavailable");
    }

    [Fact]
    public void A_strategy_that_does_not_name_the_filter_has_the_same_record_whatever_snapshot_it_is_handed()
    {
        var without = Replay(Plain, null);
        var with = Replay(Plain, EventRows.Snapshot(Rep.Id, Mon));

        RunText.Replay(with).ShouldBe(RunText.Replay(without));
        Plain.ReadsEvents.ShouldBeFalse();
        Filtered.ReadsEvents.ShouldBeTrue();
    }

    [Fact]
    public void The_run_key_differs_with_the_filter_in_the_definition_with_the_snapshot_hash_and_with_the_lag_and_a_filter_free_key_has_no_events_line()
    {
        var snapshot = EventRows.Snapshot(Rep.Id, Tue);
        var other = EventCalendarSnapshot.Create([EventRows.Meeting(Rep.Id, Tue, 1), EventRows.Meeting(Rep.Id, Fri, 2, period: new DateOnly(2026, 3, 31))], 5);
        var lagged = EventRows.Snapshot(Rep.Id, Tue, lagMinutes: 6);

        var keys = new[] { Key(Plain, null), Key(Filtered, snapshot), Key(Filtered, other), Key(Filtered, lagged) };

        keys.Distinct().Count().ShouldBe(4, "the definition, the snapshot hash and the lag each change the run key");
        lagged.Sha256.ShouldBe(snapshot.Sha256, "the lag is not in the hash of the rows; it is its own part of the key");
        Key(Filtered, snapshot).ShouldBe(Key(Filtered, EventRows.Snapshot(Rep.Id, Tue)), "equal inputs, equal key");
    }

    [Fact]
    public void The_key_text_carries_the_events_line_after_the_instruments_and_before_the_calendar_only_for_a_run_that_has_a_snapshot()
    {
        var snapshot = EventRows.Snapshot(Rep.Id, Tue);
        var replay = Replay(Filtered, snapshot);
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(Rep.Id, "TEST")]);
        var with = new RunHeader(Filtered.Record, SimulationEngine.Version, Mon, Mon, ReplayOptions.Default, false, Filtered.Fills.ToCanonicalJson(), universe, DefaultWeek(), Events: new EventFilterRecord(snapshot.Sha256, snapshot.RowCount, snapshot.LagMinutes, EventCalendarSnapshot.RuleVersion));

        var text = RunAssembler.KeyText(with, [replay.Record], []);
        var plain = RunAssembler.KeyText(with with { Events = null }, [replay.Record], []);

        text.ShouldContain("\nevents=" + snapshot.Sha256 + "|5|evf-1\ncal=");
        plain.ShouldNotContain("events=");
        text.IndexOf("i=", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("events=", StringComparison.Ordinal));
    }

    [Fact]
    public void The_assembled_run_records_the_snapshot_and_the_blocked_count_in_its_totals()
    {
        var snapshot = EventRows.Snapshot(Rep.Id, Mon);
        var replay = Replay(Filtered, snapshot);
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(Rep.Id, "TEST")]);
        var header = new RunHeader(Filtered.Record, SimulationEngine.Version, Mon, Mon, ReplayOptions.Default, false, Filtered.Fills.ToCanonicalJson(), universe, DefaultWeek(), Events: new EventFilterRecord(snapshot.Sha256, snapshot.RowCount, snapshot.LagMinutes, EventCalendarSnapshot.RuleVersion));

        var run = RunAssembler.Assemble(header, [replay]);

        run.EventFilter.ShouldBe(new EventFilterRecord(snapshot.Sha256, 1, 5, "evf-1"));
        run.Totals.Fates.EventBlocked.ShouldBe(1);
        run.Totals.Walk.EventBlocked.ShouldBe(1);
        RunText.Run(run).ShouldContain("eventFilter calendarSha256=" + snapshot.Sha256 + " rows=1 lagMinutes=5 evf=evf-1");
        RunText.Run(run).ShouldContain("eventBlocked=1 eventUnavailable=0");
    }
}
