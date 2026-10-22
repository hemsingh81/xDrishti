using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>One candidate of a comparison as the report reads it: its id, its exit (for the censor levels), the run of that candidate alone and the entries the position gate would have skipped (P4-05, D4).</summary>
/// <param name="Id">The id of the candidate (<see cref="ExitChoice.IdOf"/>).</param>
/// <param name="Exit">The exit of the candidate.</param>
/// <param name="Run">The run record of the candidate.</param>
/// <param name="GatedBlocked">The entries a gated run would have skipped under this exit; 0 for an intraday strategy.</param>
public sealed record CandidateRun(string Id, ExitCandidate Exit, SimulationRun Run, long GatedBlocked);

/// <summary>The share of the trades of a sample that left for one reason.</summary>
/// <param name="Reason">The reason.</param>
/// <param name="Trades">How many trades.</param>
/// <param name="Share">The share of the sample, 0 to 1.</param>
public sealed record ExitShare(ExitReason Reason, int Trades, double Share);

/// <summary>
/// The figures of doc 08 §3 for one sample of trades (P4-05, D6). Open-at-end trades are not in the sample (ADR 0045, D1). The unit is <c>RFilled</c> for a gross run and <c>RNet</c> for a costed one. Every ratio is null when its denominator is 0.
/// </summary>
/// <param name="Trades">Trades in the sample.</param>
/// <param name="Wins">Trades with R above 0.</param>
/// <param name="WinRate">Wins over trades.</param>
/// <param name="MeanR">Mean R.</param>
/// <param name="MedianR">Median R (the mean of the two middle values for an even count).</param>
/// <param name="DownsideDeviation"><c>sqrt(mean(min(R, 0)^2))</c>, target 0, over all trades of the sample.</param>
/// <param name="TotalR">Sum of R.</param>
/// <param name="ProfitFactor">Sum of the gains over the absolute sum of the losses; null without a loss.</param>
/// <param name="AverageHoldMinutes">Mean minutes from the entry minute to the exit minute.</param>
/// <param name="MeanCostR">Mean <c>CostR</c> of a costed run; null for a gross one.</param>
/// <param name="ExitShares">Share by exit reason, in the order of the enum, only the reasons that occur.</param>
public sealed record SampleFigures(
    int Trades,
    int Wins,
    double? WinRate,
    double? MeanR,
    double? MedianR,
    double? DownsideDeviation,
    double? TotalR,
    double? ProfitFactor,
    double? AverageHoldMinutes,
    double? MeanCostR,
    ValueList<ExitShare> ExitShares);

/// <summary>The difference of a candidate from the first candidate on the entries all candidates took (D5).</summary>
/// <param name="Entries">The number of common entries.</param>
/// <param name="MeanDifferenceR">Mean of (R of this candidate minus R of the first) over them; null when there are none.</param>
public sealed record PairedFigure(int Entries, double? MeanDifferenceR);

/// <summary>One row of the comparison table (D6): the figures on the candidate's own trades and on the common set, the paired difference, and the counters of its policy.</summary>
/// <param name="Id">The candidate id.</param>
/// <param name="Own">On the candidate's own trades.</param>
/// <param name="Common">On the entries every candidate took.</param>
/// <param name="Paired">Against the first candidate; null on the first row.</param>
/// <param name="NotCommon">Trades of this candidate that are not in the common set.</param>
/// <param name="GatedBlocked">Entries a gated run would have skipped under this exit.</param>
/// <param name="Counters">The counters of the walk worth reading beside the row, by name.</param>
public sealed record ComparisonRow(string Id, SampleFigures Own, SampleFigures Common, PairedFigure? Paired, int NotCommon, long GatedBlocked, IReadOnlyList<KeyValuePair<string, long>> Counters);

/// <summary>One figure of the MAE/MFE table (D7) with its sample size and the level at which the walk itself cuts it (D8).</summary>
/// <param name="Name">What is measured.</param>
/// <param name="Value">The figure: a share (0 to 1) or R.</param>
/// <param name="IsShare">The figure is a share, else R.</param>
/// <param name="Count">Trades that have the property (for a share), else the sample size.</param>
/// <param name="Of">The sample the figure is taken over.</param>
/// <param name="Censor">Where the walk cuts the figure: the target in R, the stop in R, or <c>none</c>.</param>
/// <param name="Censored">The figure asks for more than the censor level shows, so it is 0 or unseen by construction.</param>
/// <param name="Reading">A sentence that reads the figure, or null when the sample is below 30 trades or the figure is censored.</param>
public sealed record DiagnosticFigure(string Name, double? Value, bool IsShare, int Count, int Of, string Censor, bool Censored, string? Reading);

/// <summary>The MAE/MFE table of one candidate. A winner in it is a trade with <c>RExact</c> above 0 (the walk's own result before slippage and charges), while the comparison table uses <c>RNet</c> for a costed run and <c>RFilled</c> for a gross one.</summary>
/// <param name="Id">The candidate id.</param>
/// <param name="Trades">The candidate's trades.</param>
/// <param name="BelowMinimum">The cell has fewer than <see cref="ComparisonReport.MinimumTrades"/> trades: printed with n &lt; 30 and no reading.</param>
/// <param name="Figures">The figures in the order of doc 08 §3.</param>
public sealed record CandidateDiagnostics(string Id, int Trades, bool BelowMinimum, IReadOnlyList<DiagnosticFigure> Figures);

/// <summary>The comparison of the candidates of one run (P4-05): the table, the paired figures and the MAE/MFE diagnostics. A report is built from the run records alone; it reads no clock.</summary>
/// <param name="Unit"><c>RFilled</c> (gross) or <c>RNet</c> (costed): the unit of every R of the table.</param>
/// <param name="CommonEntries">The entries taken under every candidate.</param>
/// <param name="GateApplied">The position gate was applied to the walks; false for a swing comparison (D4).</param>
/// <param name="Rows">One row per candidate, in order.</param>
/// <param name="Diagnostics">One table per candidate, in order.</param>
public sealed record ComparisonReport(string Unit, int CommonEntries, bool GateApplied, IReadOnlyList<ComparisonRow> Rows, IReadOnlyList<CandidateDiagnostics> Diagnostics)
{
    /// <summary>The fewest trades from which a cell is read (D7).</summary>
    public const int MinimumTrades = 30;

    private const double AdverseLow = 0.8;
    private const double AdverseHigh = 1.2;
    private const double StopLevelR = 1.0;
    private const double GivebackFloorR = 1.0;

    /// <summary>Builds the report from the candidates' runs, in the order of the candidates.</summary>
    /// <param name="candidates">One or more candidates, all from one comparison (equal strategy, window, fills and costs).</param>
    /// <param name="gateApplied">False for a swing comparison, whose walks are not gated.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">No candidate.</exception>
    public static ComparisonReport Build(IReadOnlyList<CandidateRun> candidates, bool gateApplied)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("A comparison needs a candidate.", nameof(candidates));
        }

        var costed = candidates[0].Run.Costs is not null;
        var samples = candidates.Select(c => c.Run.Trades.Select(t => t.Trade).ToList()).ToList();
        var common = new HashSet<string>(samples[0].Select(t => t.Key), StringComparer.Ordinal);
        foreach (var sample in samples.Skip(1))
        {
            common.IntersectWith(sample.Select(t => t.Key));
        }

        var firstR = samples[0].Where(t => common.Contains(t.Key)).ToDictionary(t => t.Key, t => UnitR(t, costed), StringComparer.Ordinal);
        var rows = new List<ComparisonRow>(candidates.Count);
        var diagnostics = new List<CandidateDiagnostics>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            var own = samples[i];
            var shared = own.Where(t => common.Contains(t.Key)).ToList();
            PairedFigure? paired = i == 0 ? null : new PairedFigure(shared.Count, shared.Count == 0 ? null : shared.Average(t => UnitR(t, costed) - firstR[t.Key]));
            rows.Add(new ComparisonRow(candidates[i].Id, Figures(own, costed), Figures(shared, costed), paired, own.Count - shared.Count, candidates[i].GatedBlocked, CountersOf(candidates[i].Run.Totals.Walk)));
            diagnostics.Add(DiagnosticsOf(candidates[i], own));
        }

        return new ComparisonReport(costed ? "RNet" : "RFilled", common.Count, gateApplied, rows.AsReadOnly(), diagnostics.AsReadOnly());
    }

    /// <summary>R in the unit of the run. A costed run is costed in every trade (the replay sets <c>Costing</c> on each), so a costed trade without it is a broken record and is refused, not read as gross.</summary>
    private static double UnitR(SimTrade trade, bool costed) =>
        !costed ? trade.RFilled : trade.Costing?.RNet ?? throw new InvalidOperationException("A trade of a costed run has no costing; the comparison will not mix net and gross R.");

    private static SampleFigures Figures(List<SimTrade> trades, bool costed)
    {
        var n = trades.Count;
        if (n == 0)
        {
            return new SampleFigures(0, 0, null, null, null, null, null, null, null, null, new ValueList<ExitShare>([]));
        }

        var r = trades.Select(t => UnitR(t, costed)).ToList();
        var gains = r.Where(v => v > 0).Sum();
        var losses = r.Where(v => v < 0).Sum();
        var shares = trades.GroupBy(t => t.ExitReason).OrderBy(g => g.Key).Select(g => new ExitShare(g.Key, g.Count(), (double)g.Count() / n)).ToList();
        return new SampleFigures(
            n,
            r.Count(v => v > 0),
            (double)r.Count(v => v > 0) / n,
            r.Average(),
            Median(r),
            Math.Sqrt(r.Average(v => Math.Min(v, 0) * Math.Min(v, 0))),
            r.Sum(),
            losses < 0 ? gains / -losses : null,
            trades.Average(t => (t.ExitEpochSeconds - t.EntryEpochSeconds) / 60.0),
            costed ? trades.Average(t => t.Costing?.CostR ?? 0) : null,
            new ValueList<ExitShare>(shares));
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static List<KeyValuePair<string, long>> CountersOf(WalkCounters walk) =>
    [
        new("legs", walk.LegsTotal),
        new("stop_loosen_ignored", walk.StopLoosenIgnored),
        new("adds_taken", walk.AddsTaken),
        new("adds_missed", walk.AddsMissed),
        new("adds_skipped_risk", walk.AddsSkippedRisk),
        new("adds_skipped_quantity", walk.AddsSkippedQuantity),
        new("exits_time_stop", walk.ExitsTimeStop),
        new("exits_ema_exit", walk.ExitsEmaExit),
    ];

    private static CandidateDiagnostics DiagnosticsOf(CandidateRun candidate, List<SimTrade> trades)
    {
        var n = trades.Count;
        var below = n < MinimumTrades;
        var target = TargetR(candidate.Exit);
        var targetText = target is { } t ? R(t) + "R target" : "none";
        var stopText = R(StopLevelR) + "R stop";
        var winners = trades.Where(t => t.RExact > 0).ToList();
        var figures = new List<DiagnosticFigure>
        {
            Share("winners with adverse excursion above 0.8R", winners.Count(w => w.MaeR > AdverseLow), winners.Count, stopText, AdverseLow > StopLevelR, n),
            Share("winners with adverse excursion above 1.2R", winners.Count(w => w.MaeR > AdverseHigh), winners.Count, stopText, AdverseHigh > StopLevelR, n),
        };

        var medianMfe = n == 0 ? 0 : Median([.. trades.Select(x => x.MfeR)]);
        var medianCensored = target is { } medianCap && medianMfe > medianCap; // the walk cuts MFE at the target, so a median above it can only come from a record that is not of this walk
        figures.Add(n == 0
            ? new DiagnosticFigure("median MFE (R)", null, false, 0, 0, targetText, false, null)
            : new DiagnosticFigure("median MFE (R)", medianMfe, false, n, n, targetText, medianCensored, below || medianCensored ? null : "median favourable excursion of " + n.ToString(CultureInfo.InvariantCulture) + " trades"));
        foreach (var level in new[] { 1.0, 2.0, 3.0 })
        {
            figures.Add(Share("trades with MFE reaching " + R(level) + "R", trades.Count(x => x.MfeR >= level), n, targetText, target is { } cap && level > cap, n));
        }

        var runners = trades.Where(x => x.MfeR >= GivebackFloorR).ToList();
        figures.Add(runners.Count == 0
            ? new DiagnosticFigure("median giveback, MFE at least 1R (R)", null, false, 0, 0, "none", false, null)
            : new DiagnosticFigure("median giveback, MFE at least 1R (R)", Median([.. runners.Select(x => x.MfeR - x.RExact)]), false, runners.Count, runners.Count, "none", false, below ? null : "median of MFE minus the exact R over " + runners.Count.ToString(CultureInfo.InvariantCulture) + " trades that reached 1R"));
        figures.Add(Share("trades with MFE at least 1R that gave back over half of it", runners.Count(x => x.MfeR - x.RExact > x.MfeR / 2), runners.Count, "none", false, n));
        return new CandidateDiagnostics(candidate.Id, n, below, figures.AsReadOnly());

        DiagnosticFigure Share(string name, int count, int of, string censor, bool censored, int cellTrades) =>
            new(name, of == 0 ? null : (double)count / of, true, count, of, censor, censored, censored || cellTrades < MinimumTrades || of == 0 ? null : name + ": " + count.ToString(CultureInfo.InvariantCulture) + " of " + of.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The target of a candidate in R as a fixed level: <c>rr</c> of <c>FIXED_RR</c>, <c>t2_rr</c> of <c>PARTIAL_BE</c>; null for a trail, whose exit is not a level.</summary>
    private static double? TargetR(ExitCandidate exit) => exit.Policy switch
    {
        "FIXED_RR" => exit.Parameters.First(p => p.Name == "rr").Values[0],
        "PARTIAL_BE" => exit.Parameters.First(p => p.Name == "t2_rr").Values[0],
        _ => null,
    };

    private static string R(double value) => CanonicalText.Number(value);
}
