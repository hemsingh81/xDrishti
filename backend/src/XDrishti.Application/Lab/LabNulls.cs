using System.Globalization;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>What the nulls stage produced: the block for the verdict and the one ledger row of the family-level null (CF-07, AC9), written by the runner after its last cancel check.</summary>
internal sealed record NullsOutcome(NullsView View, LedgerRow Row);

/// <summary>
/// The random-entry null of a robust Lab run (spec CF-07 part A, ADR 0208). It restates each out-of-sample trade of the walk-forward winners that is an intraday fixed-target trade with a random entry minute inside the strategy's own entry window (same
/// instrument, session, side, risk fraction, target multiple, square-off, fills and cost sheet), in every seed, and reads the real mean against the seed means with <see cref="RandomEntryNull"/>. The walk is cut to the development window (CF-06): the runner gives the last session the null may read (the run's own end, or the day before the hold-out block when the run's window reaches it, which an explicit window may do),
/// the request it sends ends there, no bar after it is read and a real trade after it is not restated (it is left out and counted, and the 80 % rule decides). With <c>Enforce</c> off (the default) the outcome, the gates and the limits are exactly what they were; the figure is added beside them. Nothing is written here.
/// </summary>
internal sealed class LabNulls(IQueryHandler<RunNullWalkQuery, Result<NullWalkRun>> walks, NullOptions options, LabSettings settings)
{
    /// <summary>Appended to the run key to give the seed of the null (spec D1: the first 8 bytes of the sha256 of <c>run key + ":random-entry"</c>).</summary>
    public const string SeedSalt = ":random-entry";

    /// <summary>The method written on the ledger row of the null.</summary>
    public const string Method = "null";

    /// <summary>The trial kind of the ledger row (CF-06, D3: one trial per family-level null, not per seed).</summary>
    public const string TrialKind = "null";

    /// <summary>Less than this share of the out-of-sample trades restated and the null is unavailable: a null of a subset is not a null of the verdict.</summary>
    public const double MinCoverage = 0.8;

    /// <summary>What the figure is made over, said in the block; says so when the window was cut to stay clear of the hold-out block.</summary>
    public static string BasisOf(DateOnly developmentEnd, DateOnly runEnd) =>
        "development window only, ending " + developmentEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        + (developmentEnd < runEnd ? " (the run's window ends " + runEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " and was cut to stay clear of the hold-out block, so the null reads no hold-out session)" : " (the run's own window; it stays clear of the hold-out block)")
        + "; the out-of-sample trades of the walk-forward winners, each restated with its own target multiple; costs from the run's charge sheet at the random trade's own quantity";

    /// <summary>True when the null is computed.</summary>
    public bool IsOn => options.IsOn;

    /// <summary>The trades that will be restated, for the progress total (progress counts trades walked).</summary>
    public static int Steps(FullTestOutcome full, DateOnly developmentEnd) => Replicable(full, developmentEnd).Count;

    /// <summary>One out-of-sample trade that can be restated, with the plan position it was given.</summary>
    private sealed record Restated(NullTradeSpec Spec, SimTrade Source);

    public async ValueTask<NullsOutcome> RunAsync(LabRun run, DateTimeOffset now, SimulationRequest template, FullTestOutcome full, DateOnly developmentEnd, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(full);
        var seed = SeededRandom.SeedOf(run.Key + SeedSalt);
        var seedText = seed.ToString(CultureInfo.InvariantCulture);
        var row = LedgerRowOf(run, now);
        var restated = Replicable(full, developmentEnd);
        var oos = full.OosTrades.Count;
        var basis = BasisOf(developmentEnd, run.To);
        var notes = new List<string>
        {
            "A random trade has the real trade's instrument, session, side, risk as a fraction of the entry price, target multiple, square-off, fill rules and charge sheet; a draw that makes no trade is drawn again up to " + options.Redraws.ToString(CultureInfo.InvariantCulture) + " times and then dropped and counted.",
            "Trades of one day are correlated inside a seed (not across seeds), so the null is not wider than reality.",
            "The out-of-sample trades are winners of an exit selection, so selection bias is not nulled by this test.",
            "Intraday strategies with a fixed reward-to-risk exit only; other trades are left out and counted.",
        };
        var beyond = full.OosSources?.Count(t => t.TradeDate > developmentEnd) ?? 0;
        if (beyond > 0)
        {
            notes.Add(beyond.ToString(CultureInfo.InvariantCulture) + " out-of-sample trades are after " + developmentEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " (inside the hold-out block) and are left out: the null reads no hold-out session");
        }

        NullsOutcome Missing(string reason, int trades = 0, double? real = null, int redraws = 0, double dropped = 0) =>
            Outcome(row, notes, basis, new RandomEntryView(oos, trades, options.Draws, 0, real, null, null, null, null, null, options.Percentile, null, 0, null, options.Alpha, "<=", "unavailable", "unavailable: " + reason, dropped, redraws, seedText));

        if (restated.Count == 0)
        {
            return Missing("no out-of-sample trade is an intraday fixed-target trade with a charge sheet");
        }

        var plan = new NullWalkPlan([.. restated.Select(r => r.Spec)], options.Draws, options.Redraws, seed);
        if (FirstExit() is not { } exit)
        {
            return Missing("the candidate exits are not usable");
        }

        var request = template with { Parameters = new Dictionary<string, double>(), Exits = exit, To = developmentEnd < template.To ? developmentEnd : template.To };
        var ran = await walks.HandleAsync(new RunNullWalkQuery(request, plan, progress), cancellationToken);
        if (ran.IsFailure)
        {
            return Missing("the random-entry walk failed: " + ran.Error.Code);
        }

        var byId = ran.Value.Trades.ToDictionary(t => t.TradeId);
        var used = restated.Where(r => byId.TryGetValue(r.Spec.Id, out var d) && d.Unavailable is null && d.NetR.Count == options.Draws).ToList();
        foreach (var reason in ran.Value.Trades.Where(t => t.Unavailable is not null).Select(t => t.Unavailable!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(3))
        {
            notes.Add("left out: " + reason);
        }

        var left = oos - used.Count;
        if (left > 0)
        {
            notes.Add(left.ToString(CultureInfo.InvariantCulture) + " of " + oos.ToString(CultureInfo.InvariantCulture) + " out-of-sample trades could not be restated and are left out of both sides of the comparison");
        }

        if (used.Count < Math.Ceiling(oos * MinCoverage))
        {
            return Missing("only " + used.Count.ToString(CultureInfo.InvariantCulture) + " of " + oos.ToString(CultureInfo.InvariantCulture) + " out-of-sample trades could be restated (at least " + (MinCoverage * 100).ToString("0", CultureInfo.InvariantCulture) + " % are needed)", used.Count);
        }

        var realMean = used.Average(r => r.Source.Costing!.RNet);
        var means = new List<double>(options.Draws);
        long dropped = 0;
        for (var d = 0; d < options.Draws; d++)
        {
            double sum = 0;
            var n = 0;
            foreach (var r in used)
            {
                var value = byId[r.Spec.Id].NetR[d];
                if (double.IsNaN(value))
                {
                    dropped++;
                }
                else
                {
                    sum += value;
                    n++;
                }
            }

            if (n > 0)
            {
                means.Add(sum / n);
            }
        }

        var redraws = used.Sum(r => byId[r.Spec.Id].Redraws);
        var droppedShare = (double)dropped / ((double)used.Count * options.Draws);
        var result = RandomEntryNull.Evaluate(realMean, used.Count, options.Draws, means, droppedShare, options);
        notes.Add("dropped " + dropped.ToString(CultureInfo.InvariantCulture) + " of " + ((long)used.Count * options.Draws).ToString(CultureInfo.InvariantCulture) + " random entries after the redraws (" + redraws.ToString(CultureInfo.InvariantCulture) + " redraws in all)");
        return Outcome(row, notes, basis, new RandomEntryView(
            oos, result.Trades, result.Draws, result.ValidSeeds, result.RealMeanR, result.RandomMeanR, result.P05, result.P50, result.P95, result.P99, result.PercentileLevel, result.PercentileValue, result.CountAtLeast, result.PValue,
            result.Threshold, "<=", Word(result.Status), result.Reason, droppedShare, redraws, seedText));
    }

    /// <summary>The verdict with the block beside it; with <c>Enforce</c> on, a null that is not a pass caps a PASS at MARGINAL (never raises, and never touches the gates).</summary>
    public LabVerdictView Apply(LabVerdictView verdict, NullsView view)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(view);
        var cap = options.Enforce && !string.Equals(view.RandomEntry.Status, "pass", StringComparison.Ordinal) && string.Equals(verdict.Outcome, "PASS", StringComparison.Ordinal);
        return cap
            ? verdict with { Outcome = "MARGINAL", Limits = [.. verdict.Limits, "capped at MARGINAL by the random-entry null (enforced): " + view.RandomEntry.Status], Nulls = view with { Capped = true } }
            : verdict with { Nulls = view };
    }

    private NullsOutcome Outcome(LedgerRow row, List<string> notes, string basis, RandomEntryView entry) =>
        new(new NullsView(options.Mode.ToString(), options.Enforce, options.KeyText(), options.SnapshotHash(), entry, false, basis, notes), row);

    /// <summary>The first candidate exit of the full test, as the sweep uses it; null when the configured exits do not parse (the full test has refused such a run before this).</summary>
    private ExitChoice? FirstExit() => ExitFlags.Parse([.. settings.Exits], null, []) is { Candidates.Count: > 0 } exits ? new ExitChoice([exits.Candidates[0]], null) : null;

    /// <summary>The out-of-sample trades, on or before <paramref name="developmentEnd"/>, that are intraday fixed-target trades with a charge: a target price, no legs, a fixed-target candidate with no time-stop overlay, a costing and a risk.</summary>
    private static List<Restated> Replicable(FullTestOutcome full, DateOnly developmentEnd)
    {
        var list = new List<Restated>();
        if (full.OosSources is not { } sources)
        {
            return list;
        }

        for (var i = 0; i < sources.Count && i < full.OosTrades.Count; i++)
        {
            var t = sources[i];
            var candidate = full.OosTrades[i].CandidateId;
            if (t.TradeDate > developmentEnd || t.Legs is not null || t.TargetPrice is not { } target || t.Costing is not { } costing
                || !candidate.StartsWith("FIXED_RR{", StringComparison.Ordinal) || candidate.Contains("+time_stop", StringComparison.Ordinal))
            {
                continue;
            }

            // the risk fraction is measured from the trigger close, the price the random trade places its stop from (RandomEntryWalker); the stop must lie on the loss side of it
            var risk = Math.Abs(t.EntryPrice - t.StopPrice);
            var close = t.TriggerBar.Close;
            var fraction = Math.Abs(close - t.StopPrice) / close;
            var lossSide = t.Side == Side.Buy ? t.StopPrice < close : t.StopPrice > close;
            if (!(t.EntryPrice > 0 && risk > 0 && close > 0 && lossSide && fraction is > 0 and < 1) || !double.IsFinite(target))
            {
                continue;
            }

            var spec = new NullTradeSpec(list.Count, t.InstrumentId, t.TradeDate, t.Side, fraction, Math.Round(Math.Abs(target - t.EntryPrice) / risk, 4), costing.Segment);
            list.Add(new Restated(spec, t));
        }

        return list;
    }

    private static LedgerRow LedgerRowOf(LabRun run, DateTimeOffset now)
    {
        var id = new Guid(Convert.FromHexString(LabRunIdentity.Sha256("null|" + run.Id.ToString("N", CultureInfo.InvariantCulture))[..32]));
        return new LedgerRow(id, run.Key, run.StrategyId, run.Version, run.StrategyHash, null, run.Universe.Label, run.From, run.To, Method, now, TrialKind);
    }

    private static string Word(NullStatus status) => status switch
    {
        NullStatus.Pass => "pass",
        NullStatus.Fail => "fail",
        _ => "unavailable",
    };
}
