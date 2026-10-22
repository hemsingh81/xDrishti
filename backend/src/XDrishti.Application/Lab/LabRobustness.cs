using System.Globalization;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>What the robustness stage produced: the block for the result and the ledger rows of the sweep points (written by the runner after its last cancel check).</summary>
internal sealed record RobustnessOutcome(RobustnessView View, IReadOnlyList<LedgerRow> LedgerRows);

/// <summary>
/// The robustness checks of a <c>robust</c> run (spec P5-05, ADR 0062): a one-at-a-time sensitivity sweep (each point a plain run of the default exit, in-sample), a seeded Monte Carlo and a sign-flip baseline over the out-of-sample trades of the full test,
/// and the same trades by year. Nothing is written here and the verdict is not touched.
/// </summary>
internal sealed class LabRobustness(IQueryHandler<RunSimulationQuery, Result<SimulationRun>> simulation, LabSettings settings)
{
    /// <summary>Said in the block: why <c>by_regime</c> has no numbers.</summary>
    public const string RegimeUnavailable = "unavailable: the market-context job (P3-01d) is not built";

    /// <summary>The method written on the ledger rows of the sweep points.</summary>
    public const string SweepMethod = "sweep";

    /// <summary>The points the sweep of this strategy would run.</summary>
    public IReadOnlyList<SweepPoint> Plan(StrategyDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return SweepPlan.Build(description.Parameters, settings.Robustness.SweepParams, settings.Robustness.SweepSteps);
    }

    public async ValueTask<Result<RobustnessOutcome>> RunAsync(LabRun run, DateTimeOffset now, IReadOnlyList<SweepPoint> plan, SimulationRequest template, FullTestOutcome full, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(full);
        var errors = new List<string>();
        var exits = ExitFlags.Parse([.. settings.Exits], null, errors);
        if (exits is null || exits.Candidates.Count == 0)
        {
            return Result.Failure<RobustnessOutcome>(Error.Validation("lab.full_exits_invalid", "The candidate exits of the full test are not usable: " + string.Join("; ", errors)));
        }

        var exit = new ExitChoice([exits.Candidates[0]], null);
        var instruments = run.Universe.Instruments.Count;
        var baseResult = await PointAsync(template, exit, new SweepPoint(string.Empty, 0), new Dictionary<string, double>(), progress, 0, instruments, cancellationToken);
        var results = new List<SweepResult>();
        var rows = new List<LedgerRow>();
        for (var i = 0; i < plan.Count; i++)
        {
            var point = plan[i];
            results.Add(await PointAsync(template, exit, point, new Dictionary<string, double> { [point.Parameter] = point.Value }, progress, i + 1, instruments, cancellationToken));
            rows.Add(LedgerRowOf(run, point, now));
        }

        var judged = SensitivityAnalysis.Judge(baseResult, results);
        var sensitivity = new SensitivityView(
            template.EvaluationTimeframe,
            exits.Candidates[0].Id,
            ViewOf(baseResult),
            [.. judged.Select(j => new ParameterSensitivityView(j.Parameter, [.. j.Points.Select(ViewOf)], j.Verdict, j.Reason))],
            plan.Count == 0 ? "the strategy declares no parameter to sweep" : "in-sample, one parameter at a time, plain runs of the default exit over the whole window; not evidence, not walk-forward");

        var seed = SeededRandom.SeedOf(run.Key);
        var r = full.OosTrades.Select(t => t.R).ToList();
        var draws = settings.Robustness.McDraws;
        var mc = MonteCarlo.Run(r, draws, seed);
        var flip = SignFlipBaseline.Run(r, draws, seed);
        var limits = new List<string>
        {
            "The sweep is in-sample on the whole window: a plateau there is not evidence, a cliff is a warning.",
            "One parameter at a time: interactions are not seen.",
            "Monte Carlo treats the trades as independent: clustered losses are understated.",
            "The baseline is a sign-flip null, not a re-run with random entries.",
            "About three partial calendar years of data: the by-year figures are thin.",
            "Each sweep point is a variant in the test ledger of the family and tightens the next run.",
        };
        if (mc is null)
        {
            limits.Add("fewer than " + MonteCarlo.MinTrades.ToString(CultureInfo.InvariantCulture) + " out-of-sample trades: no Monte Carlo and no baseline");
        }

        var view = new RobustnessView(
            sensitivity,
            mc is null ? null : new MonteCarloView(mc.Trades, mc.Draws, mc.Seed.ToString(CultureInfo.InvariantCulture), mc.ObservedMeanR, mc.ObservedMaxDrawdownR, mc.MeanP05, mc.MeanP50, mc.MeanP95, mc.DrawdownP50, mc.DrawdownP95, mc.ShareNotPositive),
            [.. YearBreakdown.Of(full.OosTrades).Select(y => new YearView(y.Year, y.Trades, y.MeanR, y.TotalR, y.ProfitFactor, y.Positive))],
            flip is null ? null : new RandomBaselineView("sign_flip", flip.Trades, flip.Draws, flip.Seed.ToString(CultureInfo.InvariantCulture), flip.ObservedMeanR, flip.NullMeanP95, flip.PValue, "each trade keeps its size, its sign is a coin flip; not a random-entry run"),
            RegimeUnavailable,
            limits);
        return Result.Success(new RobustnessOutcome(view, rows));
    }

    private async ValueTask<SweepResult> PointAsync(SimulationRequest template, ExitChoice exit, SweepPoint point, Dictionary<string, double> parameters, IProgress<int>? progress, int index, int instruments, CancellationToken cancellationToken)
    {
        var request = template with { Parameters = parameters, Exits = exit };
        var ran = await simulation.HandleAsync(new RunSimulationQuery(request, progress is null ? null : new OffsetProgress(progress, index * instruments)), cancellationToken);
        if (ran.IsFailure)
        {
            return new SweepResult(point, 0, null, null, null, ran.Error.Message);
        }

        var r = ran.Value.Trades.Select(t => t.Trade.Costing is { } c ? c.RNet : t.Trade.RFilled).ToList();
        if (r.Count == 0)
        {
            return new SweepResult(point, 0, null, null, null, null);
        }

        var gains = r.Where(v => v > 0).Sum();
        var losses = -r.Where(v => v < 0).Sum();
        return new SweepResult(point, r.Count, r.Average(), losses > 0 ? gains / losses : null, r.Sum(), null);
    }

    private static SweepPointView ViewOf(SweepResult r) => new SweepPointView(r.Point.Parameter, r.Point.Value, r.Trades, r.MeanR, r.ProfitFactor, r.TotalR, r.Error);

    /// <summary>The ledger row of a sweep point: the variant hash is derived from the version's hash and the point, the run id from the run and the point, so a retry writes nothing twice.</summary>
    private static LedgerRow LedgerRowOf(LabRun run, SweepPoint point, DateTimeOffset now)
    {
        var text = point.Parameter + "=" + point.Value.ToString("R", CultureInfo.InvariantCulture);
        var hash = LabRunIdentity.Sha256(run.StrategyHash + "|" + text);
        var id = new Guid(Convert.FromHexString(LabRunIdentity.Sha256("sweep|" + run.Id.ToString("N", CultureInfo.InvariantCulture) + "|" + text)[..32]));
        return new LedgerRow(id, run.Key, run.StrategyId, run.Version, hash, run.StrategyHash, run.Universe.Label, run.From, run.To, SweepMethod, now);
    }
}
