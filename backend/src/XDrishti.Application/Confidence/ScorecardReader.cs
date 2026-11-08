using System.Globalization;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Readiness;

namespace XDrishti.Application.Confidence;

/// <summary>
/// Builds the scorecard of one strategy version from the stored results (spec CF-11 D1 to D4) and the system card (D5): the Lab evidence, the forward evidence and the inputs that have no producer yet (behind <see cref="IReadinessGaps"/>) are put into the pure calculators of
/// <c>XDrishti.Domain.Readiness</c>. Derived on every call; it writes nothing and nothing that chooses what to approve may use it (an architecture test). An input that does not exist is unavailable, which is never a pass.
/// </summary>
internal sealed class ScorecardReader(
    LabEvidenceReader lab,
    ForwardEvidenceReader forward,
    ForwardTierReader forwardTiers,
    ScorecardIntegrityReader integrityReader,
    IReadinessGaps gaps,
    PackGateResultReader packGate,
    IQueryHandler<ListStrategiesQuery, IReadOnlyList<StoredStrategy>> strategyList,
    IQueryHandler<GetStrategyQuery, Result<StrategyDetail>> strategyDetail,
    ReadinessOptions options)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The scorecard of every Lab strategy at its latest version, weakest first (then by id), without the switch and the history.</summary>
    public async ValueTask<IReadOnlyList<ScorecardView>> AllAsync(CancellationToken cancellationToken)
    {
        var cards = new List<ScorecardView>();
        foreach (var stored in await StrategiesAsync(cancellationToken))
        {
            cards.Add(await BuildAsync(stored.Id, stored.LatestVersion > 0 ? stored.LatestVersion : null, cancellationToken));
        }

        return [.. cards.OrderBy(c => c.Score).ThenBy(c => c.Strategy, StringComparer.Ordinal)];
    }

    /// <summary>The Lab strategies by id.</summary>
    public async ValueTask<IReadOnlyList<StoredStrategy>> StrategiesAsync(CancellationToken cancellationToken) =>
        [.. (await strategyList.HandleAsync(new ListStrategiesQuery(), cancellationToken)).OrderBy(s => s.Id, StringComparer.Ordinal)];

    /// <summary>The card of a strategy whose scorecard could not be built: score 0, Not ready, the reason as its hard fail, no row passing. It is what the system score counts for it; it is never stored as the strategy's snapshot.</summary>
    public static ScorecardView Unbuilt(string strategy, int? version, string reason, ReadinessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ScorecardView(
            strategy, version, 0, 0, ReadinessLabels.Code(ReadinessLabel.NotReady), ReadinessLabels.Text(ReadinessLabel.NotReady), [], [reason], [], null, null, [], 0,
            new ReadinessGateView(ReadinessGates.R, false, false, []), new ReadinessGateView(ReadinessGates.S, false, false, []), [reason], [reason], false, options.SnapshotHash(), options.KeyText(), []);
    }

    /// <summary>The weakest-first order of a set of cards.</summary>
    public static IReadOnlyList<ScorecardView> Order(IEnumerable<ScorecardView> cards) => [.. cards.OrderBy(c => c.Score).ThenBy(c => c.Strategy, StringComparer.Ordinal)];

    /// <summary>The latest version of a strategy, or null when it has none; the failure is "no such strategy".</summary>
    public async ValueTask<Result<int?>> LatestVersionAsync(string id, CancellationToken cancellationToken)
    {
        var detail = await strategyDetail.HandleAsync(new GetStrategyQuery(id), cancellationToken);
        return detail.IsFailure ? Result.Failure<int?>(ReadinessErrors.ScorecardNotFound(id, null)) : Result.Success<int?>(detail.Value.LatestVersion > 0 ? detail.Value.LatestVersion : null);
    }

    /// <summary>Whether a version of a strategy exists.</summary>
    public async ValueTask<Result<bool>> VersionExistsAsync(string id, int version, CancellationToken cancellationToken)
    {
        var detail = await strategyDetail.HandleAsync(new GetStrategyQuery(id), cancellationToken);
        if (detail.IsFailure)
        {
            return Result.Failure<bool>(ReadinessErrors.ScorecardNotFound(id, null));
        }

        return detail.Value.Versions.Any(v => v.Version == version) ? Result.Success(true) : Result.Failure<bool>(ReadinessErrors.ScorecardNotFound(id, version));
    }

    /// <summary>The scorecard of a strategy at a version (null when it has none), without the switch and the history, which the caller adds.</summary>
    public async ValueTask<ScorecardView> BuildAsync(string strategy, int? version, CancellationToken cancellationToken)
    {
        var labEvidence = await lab.ReadAsync(strategy, version, cancellationToken);
        var forwardEvidence = await forward.ReadAsync(strategy, cancellationToken);
        var benchmark = await gaps.BenchmarkBeatenAsync(strategy, version, cancellationToken);
        var twenty = await packGate.ReadAsync(strategy, version, cancellationToken);
        var integrity = await integrityReader.ReadAsync(cancellationToken);
        var (tierState, tierEvidence) = TierCEvidence.Judge(labEvidence.LabTiers, await forwardTiers.ReadAsync(strategy, cancellationToken));

        var rowNotes = new Dictionary<int, string>(labEvidence.Checklist.Notes);
        Add(rowNotes, 13, benchmark);
        Add(rowNotes, 15, twenty);
        var checklist = labEvidence.Checklist with
        {
            BenchmarkBeaten = benchmark.Value,
            TwentyTradeCheckPassed = twenty.Value,
            PhaseCheckMet = forwardEvidence.PhaseCheckMet,
            ForwardMeanR = forwardEvidence.ForwardMeanR,
            ForwardStandardError = forwardEvidence.ForwardStandardError,
            Notes = rowNotes,
        };
        var scoreInputs = forwardEvidence.Score with { Folds = labEvidence.Folds, PositiveFolds = labEvidence.PositiveFolds };
        var faults = new HardFailInputs
        {
            Integrity = integrity.State,
            CostSheet = labEvidence.CostSheet,
            FailureBoundary = forwardEvidence.FailureBoundary,
            HoldOutReused = labEvidence.HoldOutReused,
            TierCOnly = tierState,
            KillDemotion = forwardEvidence.KillDemotion,
        };
        var assessment = ReadinessAssessment.Of(checklist, scoreInputs, faults, options);
        var notes = new List<string>(labEvidence.Notes);
        notes.AddRange(forwardEvidence.Notes);

        // the evidence behind the two hard fails, always shown (a fault, a clear term, a term that does not apply and an unavailable one each say why)
        notes.Add($"Hard fail \"{ReadinessCalculator.IntegrityName}\": {integrity.Evidence}");
        notes.Add($"Hard fail \"{ReadinessCalculator.TierName}\": {tierEvidence}");
        return Map(strategy, version, assessment, notes);
    }

    /// <summary>The system card from the strategy cards.</summary>
    public async ValueTask<SystemScorecardView> SystemAsync(IReadOnlyList<ScorecardView> cards, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var strategies = new List<StrategyReadiness>(cards.Count);
        foreach (var card in cards)
        {
            var share = await gaps.RiskShareAsync(card.Strategy, cancellationToken);
            strategies.Add(new StrategyReadiness(card.Strategy, card.Score, card.WeakestSubScore, share.Value));
        }

        var integrity = (await integrityReader.ReadAsync(cancellationToken)).State;
        var system = SystemReadinessCalculator.Compute(strategies, integrity, options);
        return new SystemScorecardView(system.Score, ReadinessLabels.Code(system.Label), ReadinessLabels.Text(system.Label), system.Integrity, system.WeightedMean, system.EqualWeights, system.WeakestStrategy, system.WeakestSubScore, system.Strategies, system.Reason);
    }

    private static void Add<T>(Dictionary<int, string> notes, int row, ReadinessReading<T> reading)
        where T : struct
    {
        if (reading.Value is null && reading.Reason.Length > 0)
        {
            notes[row] = notes.TryGetValue(row, out var existing) && existing.Length > 0 ? existing + " " + reading.Reason : reading.Reason;
        }
    }

    private ScorecardView Map(string strategy, int? version, ReadinessAssessment a, List<string> notes)
    {
        var score = a.Score;
        var rows = a.Rows.Select(Row).ToList();
        var waiting = new List<string>();
        waiting.AddRange(a.Rows.Where(r => r.Status == RowStatus.Unavailable).Select(r => string.Create(Inv, $"row {r.Number} {r.Name}: waiting for {r.Producer}")));
        waiting.AddRange(score.SubScores.Where(s => s.State == SubScoreState.Unavailable).Select(s => $"{s.Name} sub-score: {s.Reading}"));
        waiting.AddRange(score.HardFails.Where(h => h.Contains("unavailable", StringComparison.Ordinal) && !h.Contains("sub-score", StringComparison.Ordinal)));
        notes.Add("Unavailable is never a pass: a row, a sub-score or a hard-fail term with no input counts as a fail, so a strategy reads Not ready until its producers exist and have run. Every threshold is a proposal and the settings hash names them.");
        return new ScorecardView(
            strategy, version, score.Score, score.RawMinimum, ReadinessLabels.Code(score.Label), ReadinessLabels.Text(score.Label),
            [.. score.SubScores.Select(s => new SubScoreView(s.Name, StateCode(s.State), s.Score, s.Reading, s.WhatWouldMoveIt))],
            score.HardFails, score.NotApplicable, score.Weakest?.Name, score.Weakest?.WhatWouldMoveIt,
            rows, a.Rows.Count(r => r.Status == RowStatus.Pass), Gate(a.GateR), Gate(a.GateS), waiting, notes,
            false, options.SnapshotHash(), options.KeyText(), []);
    }

    private static ScorecardRowView Row(ChecklistRow r) => new(r.Number, r.Name, r.Producer, StatusCode(r.Status), r.Threshold, r.Value);

    private static ReadinessGateView Gate(GateResult g) => new(g.Name, g.Passed, g.LabelOk, [.. g.Failing.Select(Row)]);

    private static string StatusCode(RowStatus status) => status switch { RowStatus.Pass => "pass", RowStatus.Fail => "fail", _ => "unavailable" };

    private static string StateCode(SubScoreState state) => state switch { SubScoreState.Scored => "scored", SubScoreState.NotApplicable => "not_applicable", _ => "unavailable" };
}
