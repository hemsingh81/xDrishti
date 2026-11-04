using System.Globalization;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>What the robustness stage produced: the block for the result, the ledger rows of the sweep points and the statistics of each point (CF-03), both written by the runner after its last cancel check.</summary>
internal sealed record RobustnessOutcome(RobustnessView View, IReadOnlyList<LedgerRow> LedgerRows, IReadOnlyList<VariantStatsRow> VariantStats);

/// <summary>
/// The robustness checks of a <c>robust</c> run (spec P5-05, ADR 0062): a one-at-a-time sensitivity sweep (each point a plain run of the default exit, in-sample), a seeded Monte Carlo and a sign-flip baseline over the out-of-sample trades of the full test,
/// and the same trades by year. Nothing is written here and the verdict is not touched.
/// </summary>
internal sealed class LabRobustness(IQueryHandler<RunSimulationQuery, Result<SimulationRun>> simulation, LabSettings settings, IContextFigures? contextFigures = null, BootstrapOptions? bootstrap = null)
{
    /// <summary>Said in the block when there is no source of regime labels at all (P6-02: with one, the text names the reason of each missing label).</summary>
    public const string RegimeUnavailable = "unavailable: no market-context source is available";

    /// <summary>XORed into the seed of the sign-flip baseline so its draws do not repeat those of the Monte Carlo.</summary>
    private const ulong FlipSeedMask = 0xA5A5_5A5A_C3C3_3C3CUL;

    /// <summary>XORed into the seed of the stationary block bootstrap so its draws differ from the Monte Carlo's and the sign-flip's (CF-03, ADR 0062 seed rule with a salt).</summary>
    private const ulong BlockSeedMask = 0x3C3C_C3C3_5A5A_A5A5UL;

    /// <summary>The method written on the ledger rows of the sweep points.</summary>
    public const string SweepMethod = "sweep";

    /// <summary>The sweep of this strategy: the parameters taken and the points it would run.</summary>
    public SweepSchedule Plan(StrategyDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return new SweepSchedule(
            SweepPlan.Swept(description.Parameters, settings.Robustness.SweepParams),
            SweepPlan.Build(description.Parameters, settings.Robustness.SweepParams, settings.Robustness.SweepSteps));
    }

    public async ValueTask<Result<RobustnessOutcome>> RunAsync(LabRun run, DateTimeOffset now, SweepSchedule schedule, SimulationRequest template, FullTestOutcome full, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(full);
        var plan = schedule.Points;
        var errors = new List<string>();
        var exits = ExitFlags.Parse([.. settings.Exits], null, errors);
        if (exits is null || exits.Candidates.Count == 0)
        {
            return Result.Failure<RobustnessOutcome>(Error.Validation("lab.full_exits_invalid", "The candidate exits of the full test are not usable: " + string.Join("; ", errors)));
        }

        var exit = new ExitChoice([exits.Candidates[0]], null);
        var instruments = run.Universe.Instruments.Count;
        var baseResult = (await PointAsync(template, exit, new SweepPoint(string.Empty, 0), new Dictionary<string, double>(), progress, 0, instruments, cancellationToken)).Result;
        var results = new List<SweepResult>();
        var rows = new List<LedgerRow>();
        var stats = new List<VariantStatsRow>();
        for (var i = 0; i < plan.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var point = plan[i];
            var ran = await PointAsync(template, exit, point, new Dictionary<string, double> { [point.Parameter] = point.Value }, progress, i + 1, instruments, cancellationToken);
            results.Add(ran.Result);
            var row = LedgerRowOf(run, point, now);
            rows.Add(row);
            // every point gets a row, also one that ran no trade or failed (n = 0, figures null), so the coverage of the ledger's variants is honest
            stats.Add(LabVariantStats.Of(run.StrategyId, row.RunId, row.StrategyHash, ran.Moments ?? TradeMoments.Of([]), SweepMethod, now));
        }

        var judged = SensitivityAnalysis.Judge(baseResult, results).ToList();
        foreach (var name in schedule.Parameters.Where(n => judged.TrueForAll(j => !string.Equals(j.Parameter, n, StringComparison.Ordinal))))
        {
            judged.Add(new ParameterSensitivity(name, [], "inconclusive", "no points: every step rounds to the declared value or is not a positive number."));
        }

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
        var flip = SignFlipBaseline.Run(r, draws, seed ^ FlipSeedMask);
        var limits = new List<string>
        {
            "The sweep is in-sample on the whole window: a plateau there is not evidence, a cliff is a warning.",
            "One parameter at a time: interactions are not seen.",
            "Monte Carlo treats the trades as independent: clustered losses are understated.",
            "The sign-flip baseline is lenient: it tests the mean R against 0 given the trade sizes, and random entries with a 2R target would lose money before costs.",
            "The out-of-sample trades are winners of an exit selection, so selection bias is not nulled by the baseline.",
            "The baseline is a sign-flip null, not a re-run with random entries.",
            "About three partial calendar years of data: the by-year figures are thin.",
            "Each sweep point is a variant in the test ledger of the family and tightens the next run.",
        };
        if (mc is null)
        {
            limits.Add("fewer than " + MonteCarlo.MinTrades.ToString(CultureInfo.InvariantCulture) + " out-of-sample trades: no Monte Carlo and no baseline");
        }

        var regime = await RegimeAsync(full, cancellationToken);
        var view = new RobustnessView(
            sensitivity,
            mc is null ? null : new MonteCarloView(mc.Trades, mc.Draws, mc.Seed.ToString(CultureInfo.InvariantCulture), mc.ObservedMeanR, mc.ObservedMaxDrawdownR, mc.MeanP05, mc.MeanP50, mc.MeanP95, mc.DrawdownP50, mc.DrawdownP95, mc.ShareNotPositive),
            [.. YearBreakdown.Of(full.OosTrades).Select(y => new YearView(y.Year, y.Trades, y.MeanR, y.TotalR, y.ProfitFactor, y.Positive))],
            flip is null ? null : new SignFlipBaselineView("sign_flip", flip.Trades, flip.Draws, flip.Seed.ToString(CultureInfo.InvariantCulture), flip.ObservedMeanR, flip.NullMeanP95, flip.PValue, "each trade keeps its size, its sign is a coin flip; not a random-entry run"),
            regime?.Text ?? RegimeUnavailable,
            limits,
            regime is null ? null : [.. regime.Buckets.Select(b => new RegimeBucketView(b.Part, b.Basis, b.Trades, b.MeanR, b.TotalR, b.Enough))],
            regime is null ? null : [.. regime.Tally.UnavailableByReason.Select(r => new RegimeMissingView(r.Key, r.Value))],
            BlockOf(full.OosTrades, seed ^ BlockSeedMask, draws, mc));
        return Result.Success(new RobustnessOutcome(view, rows, stats));
    }

    /// <summary>
    /// The stationary block bootstrap of the out-of-sample trades by entry session (CF-03, ADR 0205); null when its mode is Off. Fewer than 2 sessions with a trade, or a result that is not a finite number, gives the figure with <c>unavailable: &lt;reason&gt;</c> and no number.
    /// </summary>
    private MonteCarloBlockView? BlockOf(IReadOnlyList<SelectionTrade> trades, ulong seed, int mcDraws, MonteCarloResult? mc)
    {
        var options = bootstrap ?? new BootstrapOptions();
        if (!options.IsOn)
        {
            return null;
        }

        var draws = options.Draws ?? mcDraws;
        var settingsText = options.KeyText(draws);
        var seedText = seed.ToString(CultureInfo.InvariantCulture);
        var sessions = trades.GroupBy(t => t.Entry).OrderBy(g => g.Key).Select(g => g.Select(t => t.R).ToArray()).ToList();
        MonteCarloBlockView Missing(string reason) => new MonteCarloBlockView(options.Mode.ToString(), sessions.Count, null, draws, seedText, null, null, null, null, null, null, null, null, settingsText, "unavailable: " + reason);
        if (sessions.Count < StationaryBootstrap.MinSessions)
        {
            return Missing("fewer than " + StationaryBootstrap.MinSessions.ToString(CultureInfo.InvariantCulture) + " sessions with a trade");
        }

        if (sessions.Any(s => s.Any(v => !double.IsFinite(v))))
        {
            return Missing("a result is not a finite number");
        }

        var auto = StationaryBootstrap.AutoBlockLength(sessions.Count);
        var length = options.BlockLength > 0 ? new BlockLength(options.BlockLength, null) : auto;
        var result = StationaryBootstrap.Run(sessions, draws, length.Value, seed);
        if (result is null)
        {
            return Missing("the sessions could not be resampled");
        }

        var iidWidth = mc is null ? 0 : mc.MeanP95 - mc.MeanP05;
        var notes = new List<string>
        {
            "Whole entry sessions are resampled in blocks, so same-day and serial dependence are kept; a regime shared across weeks is not. The Monte Carlo above treats trades as independent.",
        };
        if (length.Note is not null)
        {
            notes.Add(length.Note);
        }

        if (sessions.Count < StationaryBootstrap.TrustworthySessions)
        {
            notes.Add("fewer than " + StationaryBootstrap.TrustworthySessions.ToString(CultureInfo.InvariantCulture) + " sessions with a trade: the interval is not trustworthy");
        }

        if (!(iidWidth > 0))
        {
            notes.Add("no width ratio: the iid interval has no width");
        }

        return new MonteCarloBlockView(
            options.Mode.ToString(), result.Sessions, result.BlockLength, result.Draws, seedText, result.ObservedMeanR, result.MeanP05, result.MeanP50, result.MeanP95, result.DrawdownP50, result.DrawdownP95, result.ShareNotPositive,
            iidWidth > 0 ? (result.MeanP95 - result.MeanP05) / iidWidth : null, settingsText, string.Join("; ", notes));
    }

    /// <summary>The out-of-sample trades split by the regime of their entry day (P6-02, ADR 0146); null when no source of labels is available.</summary>
    private async ValueTask<RegimeBreakdownResult?> RegimeAsync(FullTestOutcome full, CancellationToken cancellationToken)
    {
        if (contextFigures is null)
        {
            return null;
        }

        var labels = await RegimeTags.LabelsAsync(contextFigures, full.OosTrades.Select(t => t.Entry), cancellationToken);
        return RegimeBreakdown.Of([.. full.OosTrades.Select(t => (labels[t.Entry], t.R))]);
    }

    /// <summary>A sweep point's result and the moments of its trades (null when it ran no trade or failed).</summary>
    private sealed record PointRun(SweepResult Result, TradeMoments? Moments);

    private async ValueTask<PointRun> PointAsync(SimulationRequest template, ExitChoice exit, SweepPoint point, Dictionary<string, double> parameters, IProgress<int>? progress, int index, int instruments, CancellationToken cancellationToken)
    {
        var request = template with { Parameters = parameters, Exits = exit };
        var ran = await simulation.HandleAsync(new RunSimulationQuery(request, progress is null ? null : new OffsetProgress(progress, index * instruments)), cancellationToken);
        if (ran.IsFailure)
        {
            return new PointRun(new SweepResult(point, 0, null, null, null, ran.Error.Message), null);
        }

        var r = ran.Value.Trades.Select(t => t.Trade.Costing is { } c ? c.RNet : t.Trade.RFilled).ToList();
        if (r.Count == 0)
        {
            return new PointRun(new SweepResult(point, 0, null, null, null, null), null);
        }

        var gains = r.Where(v => v > 0).Sum();
        var losses = -r.Where(v => v < 0).Sum();
        return new PointRun(new SweepResult(point, r.Count, r.Average(), losses > 0 ? gains / losses : null, r.Sum(), null), TradeMoments.Of(r));
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
