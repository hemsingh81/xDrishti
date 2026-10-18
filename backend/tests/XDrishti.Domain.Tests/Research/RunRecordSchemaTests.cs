using System.Reflection;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// Criterion 13 of P3-04: the public property names of every type of the run record are pinned. A run is a stored fact that is read again later (doc 11 §6); changing a name, adding a field or removing one needs
/// <see cref="SimulationRun.CurrentSchemaVersion"/> raised, the run key with it, and an amendment of ADR 0037.
/// </summary>
public sealed class RunRecordSchemaTests
{
    private static string Properties(Type type) => type.Name + ": " + string.Join(", ", type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(p => p.Name != "EqualityContract").Select(p => p.Name));

    private static string Members(Type type) => type.Name + ": " + string.Join(", ", Enum.GetNames(type));

    [Fact]
    public void The_schema_version_is_five_since_the_overnight_order_of_p3_03e()
    {
        SimulationRun.CurrentSchemaVersion.ShouldBe(5);
    }

    [Fact]
    public void The_public_property_names_of_the_types_of_the_run_record_are_pinned()
    {
        var snapshot = new[]
        {
            Properties(typeof(SimulationRun)),
            Properties(typeof(StrategyRecord)),
            Properties(typeof(UniverseRecord)),
            Properties(typeof(UniverseMember)),
            Properties(typeof(CalendarExceptionDay)),
            Properties(typeof(InstrumentRun)),
            Properties(typeof(AppliedAdjustment)),
            Properties(typeof(DateSpan)),
            Properties(typeof(SessionCounts)),
            Properties(typeof(SessionNote)),
            Properties(typeof(DiscontinuityNote)),
            Properties(typeof(RunFates)),
            Properties(typeof(RunCounters)),
            Properties(typeof(SkipCount)),
            Properties(typeof(RunTrade)),
            Properties(typeof(ReplayOptions)),
            Properties(typeof(InstrumentReplay)),
            Properties(typeof(RunHeader)),
            Properties(typeof(InstrumentReplayInput)),
            Properties(typeof(CostRecord)),
            Properties(typeof(SizingProfile)),
            Members(typeof(SkipReason)),
            Members(typeof(SessionExclusion)),
            Members(typeof(UniverseKind)),
        };

        snapshot.ShouldBe(
        [
            "SimulationRun: SchemaVersion, RunKey, EngineVersion, Strategy, From, To, Options, AcknowledgeCsvAdjusted, Fills, Universe, CalendarExceptions, Instruments, Trades, OpenAtEnd, Totals, Costs",
            "StrategyRecord: Id, Parameters, DefinitionJson, DefinitionSha256, EvaluationTimeframe, Sides",
            "UniverseRecord: Kind, BasketId, BasketName, Label, Members",
            "UniverseMember: Id, Symbol",
            "CalendarExceptionDay: Date, IsHoliday, FromMinute, ToMinute",
            "InstrumentRun: InstrumentId, Symbol, Skipped, SkipDetail, FirstBar, LastBar, ReplayFrom, CountersCover, BarsRead, BarsFed, BarsExcluded, BarsOutsideSession, BarsSha256, TickSize, TickFromFallback, Adjustments, Sessions, Notes, NotesTruncated, Discontinuities, Scans, Walk, Fates, CandidatesInWindow, TradesInWindow",
            "AppliedAdjustment: ExDate, Kind, Numerator, Denominator, Factor, OnCsvBars",
            "DateSpan: First, Last",
            "SessionCounts: Ran, ClosedDay, SpecialSession, ShortSession, MissingSession, Excluded",
            "SessionNote: Date, Reason, BarsInWindow, ExpectedMinutes",
            "DiscontinuityNote: Date, Ratio, ExcludedPositions",
            "RunFates: EntriesSuppressed, TradesBeforeWindow, OpenAtEnd, Trades, NoFillBar, AfterSquareOff, EntryThroughStop, LimitNotReached, ExcludedAcrossDiscontinuity, PositionOpen, SkippedQuantity, SkippedCost, OrderPendingAtEnd, Total, NoTrade",
            "RunCounters: Instruments, InstrumentsRun, Skipped, BarsRead, BarsFed, BarsExcluded, BarsOutsideSession, Sessions, DiscontinuitiesFlagged, Scans, Walk, Fates, CandidatesInWindow, TradesInWindow, OpenAtEnd, EntriesSuppressed",
            "SkipCount: Reason, Instruments",
            "RunTrade: Trade, BasketIds",
            "ReplayOptions: MinSessionCompleteness, DiscontinuityThreshold",
            "InstrumentReplay: Record, Trades, OpenAtEnd",
            "RunHeader: Strategy, EngineVersion, From, To, Options, AcknowledgeCsvAdjusted, Fills, Universe, Calendar, Costs",
            "InstrumentReplayInput: InstrumentId, Symbol, RawBars, Factors, CsvAdjustedThrough, Calendar, Options, From, To, Tick, PresetSkip, SkipDetail, Costs",
            "CostRecord: SheetId, RatesSha256, SheetJson, Checked, Capital, Profile, MaxCostR",
            "SizingProfile: Name, RiskPerTradePct, MaxCapitalPerTradePct",
            "SkipReason: InstrumentNotFound, NoBars, CsvAdjustedTwice, TickSizeInvalid, NoRunnableSessions, NoSessionsInWindow, FeaturesNotReady",
            "SessionExclusion: ClosedDay, SpecialSession, ShortSession, MissingSession",
            "UniverseKind: InstrumentList, Basket",
        ]);
    }

    [Fact]
    public void The_stable_codes_of_the_skip_reasons_and_the_session_exclusions_are_the_ones_of_the_spec()
    {
        Enum.GetValues<SkipReason>().Select(r => r.ToCode()).ShouldBe(["instrument_not_found", "no_bars", "csv_adjusted_twice", "tick_size_invalid", "no_runnable_sessions", "no_sessions_in_window", "features_not_ready"]);
        Enum.GetValues<SessionExclusion>().Select(r => r.ToCode()).ShouldBe(["closed_day", "special_session", "short_session", "missing_session"]);
        UniverseLabels.MembersAsOfToday.ShouldBe("members as of today");
        UniverseLabels.InstrumentsAsListed.ShouldBe("instruments as listed");
    }

    [Fact]
    public void An_unknown_enum_value_has_no_code()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ((SkipReason)99).ToCode());
        Should.Throw<ArgumentOutOfRangeException>(() => ((SessionExclusion)99).ToCode());
        Should.Throw<ArgumentOutOfRangeException>(() => SessionCounts.Empty.Of((SessionExclusion)99));
    }
}
