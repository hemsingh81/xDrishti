#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Confidence;
using XDrishti.Application.Lab;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Paper;
using XDrishti.Application.Shadow;
using XDrishti.Application.Tests.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Readiness;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>The gaps with an answer for every input that has no producer, so that a test can show a card whose every row passes. The real source says unavailable for all of them.</summary>
internal sealed class PassingGaps : IReadinessGaps
{
    public ValueTask<ReadinessReading<double>> RiskShareAsync(string strategy, CancellationToken cancellationToken) => Number(1);

    private static ValueTask<ReadinessReading<double>> Number(double value) => ValueTask.FromResult(new ReadinessReading<double>(value, string.Empty));
}

/// <summary>An in-memory settings and snapshot store for the readiness tests.</summary>
internal sealed class FakeReadinessStore : IReadinessSettings, IReadinessSnapshotStore, IReadinessSnapshotReader
{
    public ReadinessSwitch Switch { get; set; } = new(null, false);

    public Exception? SettingsFailure { get; set; }

    public Exception? InsertFailure { get; set; }

    /// <summary>When set, an insert of a snapshot it accepts throws.</summary>
    public Func<NewReadinessSnapshot, bool>? FailInsertWhen { get; set; }

    public List<NewReadinessSnapshot> Snapshots { get; } = [];

    public ValueTask<ReadinessSwitch> CurrentAsync(CancellationToken cancellationToken) => SettingsFailure is { } failure ? throw failure : ValueTask.FromResult(Switch);

    public ValueTask<long?> InsertAsync(NewReadinessSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (InsertFailure is { } failure)
        {
            throw failure;
        }

        if (FailInsertWhen?.Invoke(snapshot) == true)
        {
            throw new InvalidOperationException("the insert was refused");
        }

        if (snapshot.Scope == "daily" && Snapshots.Any(s => s.Scope == "daily" && s.Subject == snapshot.Subject && s.StrategyId == snapshot.StrategyId && s.Version == snapshot.Version && s.TakenOn == snapshot.TakenOn))
        {
            return ValueTask.FromResult<long?>(null);
        }

        Snapshots.Add(snapshot);
        return ValueTask.FromResult<long?>(Snapshots.Count);
    }

    public ValueTask<IReadOnlyList<ReadinessSnapshotRow>> RecentAsync(string subject, string? strategyId, string? scope, int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ReadinessSnapshotRow>>([.. Snapshots.Select((s, i) => new ReadinessSnapshotRow(i + 1L, s.Subject, s.StrategyId, s.Version, s.TakenOn, DateTimeOffset.UnixEpoch, s.Scope, s.Score, s.Label, s.SettingsHash))
            .Where(r => r.Subject == subject && (strategyId is null || r.StrategyId == strategyId) && (scope is null || r.Scope == scope)).OrderByDescending(r => r.Id).Take(limit)]);
}

/// <summary>
/// The real scorecard readers over scripted producers (the Lab, the shadow record, the decay report, the sequential test, the kill proposals, the models and the changelog). By default the strategy "ORB" has a finished full Lab run, a registration, an unlocked and passing
/// hold-out, a net forward series of 330 trades and clean forward checks, so that with <see cref="PassingGaps"/> every row passes and the score is 100; a test spoils one thing at a time.
/// </summary>
internal sealed class ReadinessFixture
{
    public const string Strategy = "ORB";

    public ReadinessFixture()
    {
        Runs.HandleAsync(Arg.Any<ListLabRunsQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Result.Success<IReadOnlyList<LabRunView>>(RunList())));
        Run.HandleAsync(Arg.Any<GetLabRunQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Result.Success(RunView(Verdict))));
        Registration.HandleAsync(Arg.Any<GetRegistrationQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(RegistrationResult));
        HoldOut.HandleAsync(Arg.Any<GetHoldOutQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Result.Success(new HoldOutView(false, false, null, null, HoldOutResults))));
        Confirmation.ConfirmedOnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Confirmed ? new DateOnly(2026, 1, 1) : (DateOnly?)null));
        Shadow.ResultsAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<IReadOnlyList<ShadowResultRow>>(ShadowRows));
        Ledger.EpochAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<EpochRow?>(new EpochRow(1, new DateOnly(2025, 1, 1), DateTimeOffset.UnixEpoch, "start", "r", "a")));
        Decay.HandleAsync(Arg.Any<GetDecayReportQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(DecayResult));
        Sequential.HandleAsync(Arg.Any<GetSequentialStatusQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(SequentialResult));
        Kills.HandleAsync(Arg.Any<GetKillProposalsQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(KillsResult));
        Phase.HandleAsync(Arg.Any<EvaluatePaperPhaseCheckQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(PhaseResult));
        Integrity.HandleAsync(Arg.Any<GetCaptureIntegrityQuery>(), Arg.Any<CancellationToken>()).Returns(_ => IntegrityFailure is { } failure ? ValueTask.FromException<CaptureIntegrityView>(failure) : ValueTask.FromResult(IntegrityView));
        Instruments.FindForResolveAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(call =>
            ValueTask.FromResult<IReadOnlyList<ResolveCandidate>>([.. call.ArgAt<IReadOnlyCollection<string>>(0).Where(s => !Unresolved.Contains(s)).SelectMany(s => Enumerable.Range(0, s == Ambiguous ? 2 : 1).Select(_ => new ResolveCandidate(IdOf(s), s, s, "share", null, false, true)))]));
        BarQueries.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var id = call.Arg<Guid>();
            if (UnreadableBars.Contains(id))
            {
                throw new InvalidOperationException("the bars cannot be read");
            }

            var from = call.ArgAt<DateOnly>(1);
            var to = call.ArgAt<DateOnly>(2);
            return ValueTask.FromResult<IReadOnlyList<BarRow>>([.. Enumerable.Range(0, Math.Max(0, to.DayNumber - from.DayNumber)).Select(i => DayBar(from.AddDays(i), TradedValue(id)))]);
        });
        Models.ListAsync(Arg.Any<ModelRole?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<IReadOnlyList<StoredModel>>(Champions));
        Strategies.HandleAsync(Arg.Any<ListStrategiesQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<IReadOnlyList<StoredStrategy>>(StrategyList));
        StrategyDetails.HandleAsync(Arg.Any<GetStrategyQuery>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(StrategyList.FirstOrDefault(s => s.Id == call.Arg<GetStrategyQuery>().Id) is { } s
            ? Result.Success(new StrategyDetail(s.Id, "intraday", false, DateTimeOffset.UnixEpoch, s.LatestVersion, [.. Enumerable.Range(1, s.LatestVersion).Select(v => new VersionSummary(v, "h", null, null, DateTimeOffset.UnixEpoch))]))
            : Result.Failure<StrategyDetail>(LabErrors.NotFound(call.Arg<GetStrategyQuery>().Id))));
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));

    public IQueryHandler<ListLabRunsQuery, Result<IReadOnlyList<LabRunView>>> Runs { get; } = Substitute.For<IQueryHandler<ListLabRunsQuery, Result<IReadOnlyList<LabRunView>>>>();

    public IQueryHandler<GetLabRunQuery, Result<LabRunView>> Run { get; } = Substitute.For<IQueryHandler<GetLabRunQuery, Result<LabRunView>>>();

    public IQueryHandler<GetRegistrationQuery, Result<RegistrationView>> Registration { get; } = Substitute.For<IQueryHandler<GetRegistrationQuery, Result<RegistrationView>>>();

    public IQueryHandler<GetHoldOutQuery, Result<HoldOutView>> HoldOut { get; } = Substitute.For<IQueryHandler<GetHoldOutQuery, Result<HoldOutView>>>();

    public ICostConfirmationFact Confirmation { get; } = Substitute.For<ICostConfirmationFact>();

    public IShadowResultReader Shadow { get; } = Substitute.For<IShadowResultReader>();

    public IPaperLedgerData Ledger { get; } = Substitute.For<IPaperLedgerData>();

    public IQueryHandler<GetDecayReportQuery, Result<DecayReportView>> Decay { get; } = Substitute.For<IQueryHandler<GetDecayReportQuery, Result<DecayReportView>>>();

    public IQueryHandler<GetSequentialStatusQuery, Result<SequentialStatusView>> Sequential { get; } = Substitute.For<IQueryHandler<GetSequentialStatusQuery, Result<SequentialStatusView>>>();

    public IQueryHandler<GetKillProposalsQuery, Result<KillProposalsView>> Kills { get; } = Substitute.For<IQueryHandler<GetKillProposalsQuery, Result<KillProposalsView>>>();

    public IQueryHandler<EvaluatePaperPhaseCheckQuery, Result<PaperPhaseCheckView>> Phase { get; } = Substitute.For<IQueryHandler<EvaluatePaperPhaseCheckQuery, Result<PaperPhaseCheckView>>>();

    public IQueryHandler<GetCaptureIntegrityQuery, CaptureIntegrityView> Integrity { get; } = Substitute.For<IQueryHandler<GetCaptureIntegrityQuery, CaptureIntegrityView>>();

    public IInstrumentQueries Instruments { get; } = Substitute.For<IInstrumentQueries>();

    public IBarQueries BarQueries { get; } = Substitute.For<IBarQueries>();

    public IModelRegistry Models { get; } = Substitute.For<IModelRegistry>();

    public IQueryHandler<ListStrategiesQuery, IReadOnlyList<StoredStrategy>> Strategies { get; } = Substitute.For<IQueryHandler<ListStrategiesQuery, IReadOnlyList<StoredStrategy>>>();

    public IQueryHandler<GetStrategyQuery, Result<StrategyDetail>> StrategyDetails { get; } = Substitute.For<IQueryHandler<GetStrategyQuery, Result<StrategyDetail>>>();

    public ChangelogHandlersTests.FakeChangelog Log { get; } = new();

    public FakeReadinessStore Store { get; } = new();

    public IReadinessGaps Gaps { get; set; } = new PassingGaps();

    /// <summary>The owner's newest pack gate record the reader finds; a PASS over 20 trades unless a test says otherwise (null = none recorded).</summary>
    public PackGateResultRow? PackGateRow { get; set; } = new(1, "pack-1", "PASS", 20, DateTimeOffset.UnixEpoch);

    public Exception? PackGateFailure { get; set; }

    public PackGateResultReader PackGate => new(new FakePackGateResults(this), NullLogger<PackGateResultReader>.Instance);

    private sealed class FakePackGateResults(ReadinessFixture owner) : IPackGateResultReader
    {
        public ValueTask<PackGateResultRow?> LatestAsync(string strategyId, int version, CancellationToken cancellationToken) =>
            owner.PackGateFailure is { } failure ? throw failure : ValueTask.FromResult(owner.PackGateRow);
    }

    public ReadinessOptions Options { get; set; } = new();

    // ---- scripted producers a test may spoil ----

    public LabVerdictView? Verdict { get; set; } = GoodVerdict();

    public Result<RegistrationView> RegistrationResult { get; set; } = Result.Success(new RegistrationView(Strategy, 1, "c", "s", "a", "e", "f", new string('a', 64), DateTimeOffset.UnixEpoch, "owner:me@x.test"));

    public IReadOnlyList<HoldOutResultView> HoldOutResults { get; set; } = [new HoldOutResultView(Strategy, 1, "unlock", "m", "done", DateTimeOffset.UnixEpoch, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 1), false, false, true, "pass", "kept 80 %", 0.2, 0.16, 0.8, 40, "p", "s", "owner:me@x.test", [])];

    public bool Confirmed { get; set; } = true;

    public IReadOnlyList<ShadowResultRow> ShadowRows { get; set; } = Series(330);

    public Result<DecayReportView> DecayResult { get; set; } = Result.Success(DecayView());

    public Result<SequentialStatusView> SequentialResult { get; set; } = Result.Success(SequentialView("on_track", null));

    public Result<KillProposalsView> KillsResult { get; set; } = Result.Success(new KillProposalsView([], new KillSettingsView(false, null, 1.5, 2, "h"), []));

    public Result<PaperPhaseCheckView> PhaseResult { get; set; } = Result.Success(new PaperPhaseCheckView(new PaperPhaseReport(null, default, 0, [], "MET", "MET", [], [], null!, []), "text"));

    /// <summary>Criterion I of the phase check as the narrow query says it: MET by default (clear).</summary>
    public CaptureIntegrityView IntegrityView { get; set; } = new("MET", "MET: no replay disagreed with a stored entry and nothing stays pending", 40, new DateOnly(2026, 11, 6), []);

    /// <summary>When set, the narrow read of criterion I throws it.</summary>
    public Exception? IntegrityFailure { get; set; }

    /// <summary>When set, the shadow rows read that the forward tier reader makes (and only that one) throws.</summary>
    public Exception? TierShadowFailure { get; set; }

    /// <summary>The 20-day median traded value (rupees) the day bars of an instrument give; tier A (Rs 200 crore) by default. A test sets it per symbol with <see cref="ValueBySymbol"/>.</summary>
    public decimal DefaultTradedValue { get; set; } = 200m * LiquidityTiers.Crore;

    public Dictionary<string, decimal> ValueBySymbol { get; } = new(StringComparer.Ordinal);

    /// <summary>Symbols the master list does not know.</summary>
    public HashSet<string> Unresolved { get; } = new(StringComparer.Ordinal);

    /// <summary>The one symbol the master list names twice (ambiguous).</summary>
    public string? Ambiguous { get; set; }

    /// <summary>Instruments whose day bars cannot be read.</summary>
    public HashSet<Guid> UnreadableBars { get; } = [];

    public IReadOnlyList<StoredModel> Champions { get; set; } = [];

    /// <summary>A stable instrument id for a symbol (its first 16 characters as bytes).</summary>
    public static Guid IdOf(string symbol) => new(System.Text.Encoding.UTF8.GetBytes(symbol.PadRight(16, '_')[..16]));

    private decimal TradedValue(Guid id) => ValueBySymbol.Where(p => IdOf(p.Key) == id).Select(p => (decimal?)p.Value).FirstOrDefault() ?? DefaultTradedValue;

    private static BarRow DayBar(DateOnly date, decimal value) => new(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset), 100, 100, 100, 100, (long)(value / 100m), 375, true, null);

    public IReadOnlyList<StoredStrategy> StrategyList { get; set; } = [new StoredStrategy(Strategy, "intraday", false, DateTimeOffset.UnixEpoch, 1, 1)];

    // ---- the real readers over them ----

    public ForwardEdgeReader ForwardEdge => new(Shadow, Ledger, Confirmation);

    public LabEvidenceReader LabReader => new(Runs, Run, Registration, HoldOut, Confirmation);

    public ForwardEvidenceReader ForwardReader => new(ForwardEdge, Decay, Sequential, Kills, async cancellationToken => PaperPhaseVerdict.Met(await Phase.HandleAsync(new EvaluatePaperPhaseCheckQuery(null, null), cancellationToken)), Models, new StrategyCells(Log));

    private ForwardEdgeReader TierEdge
    {
        get
        {
            if (TierShadowFailure is not { } failure)
            {
                return ForwardEdge;
            }

            var broken = Substitute.For<IShadowResultReader>();
            broken.ResultsAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromException<IReadOnlyList<ShadowResultRow>>(failure));
            return new ForwardEdgeReader(broken, Ledger, Confirmation);
        }
    }

    public ForwardTierReader ForwardTiers => new(TierEdge, Instruments, BarQueries, new CostStressOptions(), NullLogger<ForwardTierReader>.Instance);

    public ScorecardIntegrityReader IntegrityReader => new(Integrity, NullLogger<ScorecardIntegrityReader>.Instance);

    public ScorecardReader Reader => new(LabReader, ForwardReader, ForwardTiers, IntegrityReader, Gaps, PackGate, Strategies, StrategyDetails, Options);

    public ReadinessGate Gate => new(Reader, Store, Store, Options, Time, NullLogger<ReadinessGate>.Instance);

    public SnapshotScorecardsHandler Snapshot => new(Reader, Store, Options, Time, NullLogger<SnapshotScorecardsHandler>.Instance);

    public GetScorecardHandler Get => new(Reader, Store, Store);

    public ListScorecardsHandler List => new(Reader, Store, new ReadinessOptionsView(Options));

    // ---- builders ----

    /// <summary>A net, confirmed forward series: one candidate a day, R alternating +0.5 and -0.1 (mean +0.2 R, sd about 0.3), over <paramref name="count"/> sessions.</summary>
    public static List<ShadowResultRow> Series(int count) =>
    [
        .. Enumerable.Range(0, count).Select(i => new ShadowResultRow(
            new DateOnly(2025, 1, 1).AddDays(i), $"{Strategy}|S{i}|BUY|{i}", "g", true, "graded", i % 2 == 0 ? 0.5 : -0.1, "net", "sheet-1", i % 2 == 0 ? 0.6 : 0.0, 1)),
    ];

    private IReadOnlyList<LabRunView> RunList() => Verdict is null ? [] : [RunView(null)];

    private static LabRunView RunView(LabVerdictView? verdict) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), "k", "succeeded", null, new LabStrategyRef(Strategy, 1, "h"), null!, null!, LabSettings.RobustMethod, null!, [], null, [], [], [], null, false,
        DateTimeOffset.UnixEpoch, null, DateTimeOffset.UnixEpoch.AddDays(1), verdict);

    public static LabVerdictView GoodVerdict() => new(
        "PASS", 2, 1.25,
        new EvidenceView(330, 0.20, 1.4, 6, 5, 5, 0, "wf", new DependenceView("ReportOnly", 330, 320, 1.03, "cluster", null, null, null, null, null, null, string.Empty, string.Empty)),
        [], [], [new CellView("c", "15m", "BUY", "d", "d", null, true, string.Empty, 6, 6, 40, 0.2, 0.1)], [], [],
        new RobustnessView(
            new SensitivityView("15m", "FIXED_RR", null, [], string.Empty),
            new MonteCarloView(330, 1000, "s", 0.2, 5, 0.1, 0.2, 0.3, 4, 12, 0.1, 9),
            [new YearView(2024, 100, 0.2, 20, 1.3, true), new YearView(2025, 120, 0.2, 24, 1.3, true), new YearView(2026, 110, 0.2, 22, 1.3, true)],
            null, string.Empty, [], null, null, null,
            [new QuarterView(2025, 1, 60, 0.2), new QuarterView(2025, 2, 60, 0.2), new QuarterView(2025, 3, 60, 0.2), new QuarterView(2025, 4, 60, -0.3), new QuarterView(2026, 1, 90, 0.2)],
            0.10),
        new DeflatedSharpeView("ReportOnly", 2, 2, null, null, 0.2, 320, null, null, 0.99, 0.95, string.Empty, string.Empty, string.Empty),
        new CostStressView(
            "ReportOnly", false, string.Empty, string.Empty, true, "sheet-1",
            [new CostStressRowView(1.0, 330, 0.2, 0, ">", "pass", string.Empty), new CostStressRowView(1.5, 330, 0.15, 0.08, ">=", "pass", string.Empty), new CostStressRowView(2.0, 330, 0.1, 0, ">", "pass", string.Empty)],
            "pass", new CostStressDelayView(1, 100, 90, 0.2, 0.18, 0.9, 0.7, "pass", string.Empty, string.Empty), false, string.Empty, string.Empty, null, new CostStressTiersView(330, 300, 30, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty, 0, 330, 0, 5000)),
        null,
        new NullsView(
            "ReportOnly", false, string.Empty, string.Empty,
            new RandomEntryView(330, 330, 1000, 1000, 0.2, 0, 0, 0, 0.1, 0.12, 95, 0.1, 0, 0.01, 0.05, "<=", "pass", string.Empty, 0, 0, "s"),
            false, string.Empty, [],
            new PlaceboSetView(21, 20, 19, 0.2, 1, 0.04, 0.05, "<=", "pass", string.Empty, []),
            new MirrorView(330, -0.2, 0.2, 0, "<=", "pass", string.Empty),
            new PlateauView("ReportOnly", false, string.Empty, string.Empty, 0.7, 3, ">=", "pass", string.Empty, false, string.Empty, [])),
        new BenchmarkComparisonView(250, 20, 0.12, 0.05, string.Empty, string.Empty));

    /// <summary>A clean decay report: a net reference of 40 trades at +0.1 R, sigma 1, and no CUSUM alarm.</summary>
    public static DecayReportView DecayView(bool alarm = false) => new(
        Strategy, 1, true, new DecaySettingsView(60, 1.5, -0.05, new string('a', 64), "s", 0.3, true), new DecayReferenceView("available", null, 40, 0.1, 1, "net"),
        new DecaySeriesView("basis", null, "ok", 330, 0, 0, false, null, [], new DecayCusumView(alarm, 0, null), [], null), null!, [], [], "line");

    public static SequentialStatusView SequentialView(string state, int? firstFailure) => new(
        "strategy", Strategy, 1, true, new SequentialBoundariesView(-2, -4, 0, 7, 55), 0.3, true, new string('a', 64), "s",
        new SequentialSeriesView("basis", null, state, state, 0, 330, 320, null, null, firstFailure, null, 0, null, new SequentialPathView(0, 0, 1)), null!, [], "line");

    public static StoredModel Champion(double ecePp) => new(
        1, ModelRole.Champion,
        new ModelToRegister("v", "s", 1, "t", null, "ts", "f", 1, 1, 1, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 1), null, ValidationOutcome.Eligible, 6, 6, 5, true, ecePp, 0.5, 0.2, string.Empty, string.Empty, string.Empty, [], [], [], 1, 0, DateTimeOffset.UnixEpoch, "x"),
        1);
}
