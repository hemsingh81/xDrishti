using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Paper;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The decay monitor of one strategy (spec CF-12 D1 and the kill-rule evaluations of D2, part 12a, ADR 0218). It assembles the edge series with the same rule as the sequential failure test (<see cref="EdgeSeries"/>) and the P10 behavioural series beside it, reads the
/// strategy's reference from the point-in-time backtest of P10-04 (<see cref="BacktestReferenceReader"/>), and hands them to the pure <see cref="DecayMonitor"/> and <see cref="KillRules"/>. Derived on every request; nothing is stored, proposed, paused or executed, and
/// nothing that chooses what to approve may use it (an architecture test). Where an input has no source the answer says <c>unavailable</c> and why.
/// </summary>
internal sealed class GetDecayReportHandler(
    IShadowResultReader reader,
    IPaperLedgerData ledger,
    IBacktestPaperData paperData,
    ICostConfirmationFact confirmation,
    BacktestReferenceReader references,
    DecayOptions options,
    SequentialOptions sequential) : IQueryHandler<GetDecayReportQuery, Result<DecayReportView>>
{
    private static readonly DriftOptions Drift = new();

    public async ValueTask<Result<DecayReportView>> HandleAsync(GetDecayReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var strategy = query.Strategy?.Trim();
        if (string.IsNullOrEmpty(strategy))
        {
            return Result.Failure<DecayReportView>(PaperErrors.Invalid("strategy", "The decay report needs the strategy id."));
        }

        if (strategy.Contains('|', StringComparison.Ordinal))
        {
            return Result.Failure<DecayReportView>(PaperErrors.Invalid("strategy", "A strategy id holds no `|`."));
        }

        var prefix = strategy + "|";
        var epoch = await ledger.EpochAsync(null, cancellationToken);
        var rho = EdgeCount.PlaceholderRho; // CF-08 and CF-09 use the placeholder until CF-01 estimates the same-day correlation; so does this report
        var reference = epoch is null ? Reference.None("there is no paper epoch yet, so there is no point in time to take the backtest reference at") : await ReadReferenceAsync(strategy, epoch.StartedOn, cancellationToken);
        var edge = await EdgeAsync(epoch?.Id, prefix, reference, rho, cancellationToken);
        var behavioural = await BehaviouralAsync(epoch?.Id, prefix, reference, rho, cancellationToken);
        var kill = Kill(edge);
        var view = new DecayReportView(
            strategy, epoch?.Id, edge.HasSeries || behavioural.Trades > 0,
            new DecaySettingsView(options.WindowTrades, options.Sigmas, options.QuarterFloorR, options.SnapshotHash(), options.KeyText(), rho, true),
            reference.View, edge.View, behavioural, kill, Statements(options, reference), string.Empty);
        return Result.Success(view with { Line = Line(view) });
    }

    private async ValueTask<Reference> ReadReferenceAsync(string strategy, DateOnly start, CancellationToken cancellationToken)
    {
        // shortcut: the reference is the strategy's backtest trades before the epoch's start read by P10-04 (BacktestReferenceReader), which includes in-sample trades; the spec asks for the out-of-sample plus hold-out trades of the Lab result, for which there is no
        // per-strategy port yet. In-sample trades typically make mu_ref higher, so the monitor typically alerts earlier, not later.
        // shortcut: BacktestReferenceReader reads the trades of every source run of the snapshot and this filters to the strategy afterwards; filter the source runs by strategy when the snapshot grows.
        var read = await references.ReadAsync(start, cancellationToken);
        if (read.Problem is not null)
        {
            return Reference.None(read.Problem);
        }

        var cells = read.Cells.Where(c => c.Key.StartsWith(strategy + "|", StringComparison.Ordinal)).Select(c => c.Value).ToList();
        if (cells.Count == 0 || cells.All(c => c.Results.Count == 0))
        {
            return Reference.None("no backtest trade of this strategy before the epoch's start");
        }

        var costed = cells.Where(c => c.Results.Count > 0).Select(c => c.Costed).Distinct().ToList();
        if (costed.Count != 1)
        {
            return Reference.None("the strategy's backtest cells are not all costed the same way, so there is no single reference basis");
        }

        var observations = new List<DriftObservation>();
        foreach (var cell in cells.OrderBy(c => c.SheetId, StringComparer.Ordinal))
        {
            foreach (var r in cell.Results)
            {
                observations.Add(new DriftObservation("r" + observations.Count.ToString(CultureInfo.InvariantCulture), observations.Count, r));
            }
        }

        var band = RollingBandDetector.MakeBand(observations, Drift);
        return band.IsFailure
            ? Reference.None(band.Error.Message)
            : Reference.Of(band.Value, costed[0] ? "net" : "gross");
    }

    private async ValueTask<EdgeRead> EdgeAsync(long? epochId, string prefix, Reference reference, double rho, CancellationToken cancellationToken)
    {
        // shortcut: every shadow row of every epoch is read and filtered in memory; filter by edge_epoch (and trade key) in SQL when the record grows.
        var rows = await reader.ResultsAsync(cancellationToken);
        var selection = EdgeSeries.Select(rows, epochId, prefix);
        var costs = await ShadowCostBasis.ReadAsync(selection.Graded, confirmation, cancellationToken);
        var figures = EdgeSeries.Figures(selection, costs);
        var ordered = figures.Trades;
        var noFigure = figures.NoFigure;
        var trades = ordered.Select(x => new DecayTrade(x.Row.TradeKey, EdgeSeries.SessionInstant(x.Row.SessionDate), x.Row.SessionDate, x.R, x.DayCandidates)).ToList();
        var basis = EdgeSeries.Basis(costs);
        var notes = new List<string>();
        if (selection.ScopedCount == 0)
        {
            notes.Add("No shadow candidate of the current paper epoch counts for edge for this strategy: there is no series.");
        }
        else if (trades.Count == 0 && selection.HeldBack == 0)
        {
            notes.Add("No shadow trade of the current paper epoch is graded yet.");
        }

        if (selection.FirstPending is { } pending)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"The series stops before the session {pending:yyyy-MM-dd}, which still has a candidate without a grade; {selection.HeldBack} graded trade(s) from that session on are held back."));
        }

        if (noFigure > 0)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{noFigure} graded trade(s) have no figure on this basis and are left out of the series; they still count among the day's candidates for the design effect."));
        }

        var view = Series(basis, notes, trades, costs.Gross ? "gross" : "net", reference, rho, out var decay);
        var costRows = costs.Gross ? null : ordered.Where(x => x.Row.RGross is not null).Select(x => (Net: x.Row.RSim!.Value, Cost: x.Row.RGross!.Value - x.Row.RSim!.Value)).ToList();
        return new EdgeRead(selection.ScopedCount > 0, view, decay, trades, ordered.Select(x => x.R).ToList(), costs.Gross, costRows is { } c && c.Count == ordered.Count ? c : null);
    }

    private async ValueTask<DecaySeriesView> BehaviouralAsync(long? epochId, string prefix, Reference reference, double rho, CancellationToken cancellationToken)
    {
        const string Label = "P10 behavioural series: the owner-approved, armed, primary, measured trades of the epoch, in exit order";
        if (epochId is not { } id)
        {
            return Series(Label, ["No paper epoch yet."], [], "net", reference, rho, out _);
        }

        var data = await paperData.ReadAsync(id, cancellationToken);
        var counted = BehaviouralSeries.Select(data, prefix);
        var trades = counted.Select(t => new DecayTrade(string.Create(CultureInfo.InvariantCulture, $"ticket-{t.Trade.TicketId:D12}"), t.Trade.ExitAt.ToUnixTimeSeconds(), t.ExitDay, t.Trade.R, t.DayCandidates)).ToList();
        var allNet = counted.All(t => t.Trade.RBasis == "net");
        var allGross = counted.All(t => t.Trade.RBasis == "gross");
        var notes = new List<string>();
        if (counted.Count == 0)
        {
            notes.Add("No counted paper trade of this strategy in the epoch yet.");
        }

        var basis = Label + "; " + (counted.Count == 0 ? "none counted yet" : allNet ? "net R as the P10 count stores it" : allGross ? "gross R (before charges)" : "net and gross R mixed");
        return Series(basis, notes, trades, allNet ? "net" : allGross ? "gross" : "mixed", reference, rho, out _);
    }

    private DecaySeriesView Series(string basis, List<string> notes, List<DecayTrade> trades, string seriesBasis, Reference reference, double rho, out DecayReport? report)
    {
        report = null;
        string state;
        if (trades.Count == 0)
        {
            state = "no_series";
        }
        else if (reference.Band is null)
        {
            state = "unavailable";
            notes.Add("unavailable: " + reference.View.Reason);
        }
        else if (reference.View.Basis != seriesBasis)
        {
            state = "unavailable";
            notes.Add($"unavailable: the reference is {reference.View.Basis} of costs but the series is {seriesBasis}; they are not compared.");
        }
        else
        {
            report = DecayMonitor.Evaluate(trades, reference.Band, options, rho, Drift);
            state = report.Windows.Count == 0 ? "monitoring" : report.Windows[^1].Below ? "alert" : "ok";
        }

        var note = notes.Count == 0 ? null : string.Join(' ', notes);
        if (report is null)
        {
            return new DecaySeriesView(basis, note, state, trades.Count, trades.Count, 0, false, null, [], null, [], null);
        }

        return new DecaySeriesView(
            basis, note, state, report.Trades, report.PendingTrades, report.Streak, report.TwiceInARow, report.FirstTwiceAtWindow,
            [.. report.Windows.Select(w => new DecayWindowView(w.Index, w.Trades, w.FirstDay, w.LastDay, w.MeanR, w.DesignEffect, w.Threshold, w.Below))],
            new DecayCusumView(report.CusumAlarm, report.CusumEvents.Count, report.CusumEvents.Count == 0 ? null : report.CusumEvents[^1].Evidence),
            [.. report.Quarters.Select(q => new DecayQuarterView(string.Create(CultureInfo.InvariantCulture, $"{q.Year}-Q{q.Quarter}"), q.Trades, q.MeanR, q.Weak))],
            report.WeakQuarterShare);
    }

    private List<DecayKillView> Kill(EdgeRead edge)
    {
        var verdicts = new List<KillRuleVerdict>();

        // (a) cost stress needs the net R and the cost of each trade; a gross series has no charges in it, so there is nothing to stress
        if (edge.Gross || edge.Costs is null)
        {
            verdicts.Add(KillRules.NotEvaluated("cost_stress", edge.R.Count, "cost stress not evaluated: the series is gross of charges, or a trade has no gross figure to take the charges from"));
        }
        else
        {
            // shortcut: c is the charges in R (gross R minus net R); the slippage of the modelled fill is inside both and is not recorded apart, so c is understated and the stress is lighter than CF-05's. There is also no store of when the rule last fired
            // (part 12b), so the "20 new trades since it last fired" wait is not applied here.
            var verdict = KillRules.CostStress([.. edge.Costs.Select(x => x.Net)], [.. edge.Costs.Select(x => x.Cost)]);
            verdicts.Add(verdict with { Text = verdict.Text + " (charges only: the slippage inside the modelled fill is not recorded apart)" });
        }

        // (b) drawdown needs the 95th percentile of the matched drawdown at the series' own n
        // shortcut: BacktestBand.Compute does give DrawdownP95 at the paper set's own n, but it matches draws by the cell of each trade and a shadow row records no cell (only the trade key strategy|symbol|side|date), so it cannot be asked for the edge series.
        verdicts.Add(KillRules.Drawdown(edge.R, null) with { Text = "drawdown not evaluated: the shadow record has no cell for a trade, so the matched draws at the series' own n cannot be made (the P10-04 band code can give the 95th percentile at an arbitrary n once the cells are known)" });

        // (c) decay twice in a row
        verdicts.Add(edge.Decay is null
            ? KillRules.NotEvaluated("decay_twice", edge.Trades.Count, "decay twice in a row not evaluated: the series has no reference or no trade")
            : KillRules.DecayTwice(edge.Decay));

        // (d) the CF-09 failure boundary, as a proposal-only input
        if (edge.Trades.Count == 0)
        {
            verdicts.Add(KillRules.FailureBoundary(null, null, sequential.FailureBoundary, 0));
        }
        else
        {
            var run = SequentialFailureTest.Run([.. edge.Trades.Select(t => new SequentialTrade(t.R, t.DayCandidates))], sequential, EdgeCount.PlaceholderRho);
            verdicts.Add(KillRules.FailureBoundary(run.State, run.Sum, sequential.FailureBoundary, run.Trades));
        }

        return [.. verdicts.Select(v => new DecayKillView(v.Rule, State(v.State), v.Value, v.Limit, v.Trades, v.ProposalOnly, v.Text))];
    }

    private static string State(KillRuleState state) => state switch
    {
        KillRuleState.Holds => "holds",
        KillRuleState.Fires => "fires",
        KillRuleState.Held => "held",
        _ => "not_evaluated",
    };

    private static List<string> Statements(DecayOptions o, Reference reference) =>
    [
        string.Create(CultureInfo.InvariantCulture, $"Windows of {o.WindowTrades} trades do not overlap, so one run of bad trades is one event. A window alerts when its mean R is below mu_ref - {o.Sigmas:0.0#} x sigma x sqrt(DEFF / {o.WindowTrades} + 1 / N_ref); two completed windows in a row below their levels is the decay alert."),
        string.Create(CultureInfo.InvariantCulture, $"DEFF is the design effect of the days' candidates with a same-day correlation of {EdgeCount.PlaceholderRho:0.00}, a placeholder until CF-01 estimates it."),
        reference.View.State == "available"
            ? "The reference is the strategy's backtest trades before the epoch started, including in-sample trades (the Lab result's out-of-sample and hold-out trades are not available per strategy yet), which typically makes an alert earlier, not later."
            : "No reference: nothing is judged. unavailable is never read as fine.",
        "Report only: this part proposes, pauses and changes nothing, and places no order. The kill rules are evaluations with their numbers; their proposals, the auto setting and re-enabling are later parts. Their thresholds are proposals of the spec.",
    ];

    private static string Line(DecayReportView v)
    {
        string Part(DecaySeriesView s) => s.State switch
        {
            "alert" => string.Create(CultureInfo.InvariantCulture, $"alert, {s.Streak} window(s) below in a row{(s.TwiceInARow ? " (twice in a row)" : string.Empty)}, {s.Trades} trade(s)"),
            "ok" or "monitoring" => string.Create(CultureInfo.InvariantCulture, $"{s.State}, {s.Windows.Count} completed window(s), {s.Trades} trade(s)"),
            _ => s.State,
        };

        var fires = v.Kill.Count(k => k.State == "fires");
        return string.Create(CultureInfo.InvariantCulture, $"decay monitor of {v.Strategy} (report only): edge series {Part(v.Edge)}; behavioural series {Part(v.Behavioural)}; {fires} kill rule evaluation(s) firing (proposals only, none stored); settings {v.Settings.SettingsHash[..12]}");
    }

    private sealed record Reference(ExpectancyBand? Band, DecayReferenceView View)
    {
        public static Reference None(string reason) => new(null, new DecayReferenceView("unavailable", reason, null, null, null, null));

        public static Reference Of(ExpectancyBand band, string basis) => new(band, new DecayReferenceView("available", null, band.ReferenceTrades, band.Mean, band.Sd, basis));
    }

    private sealed record EdgeRead(bool HasSeries, DecaySeriesView View, DecayReport? Decay, List<DecayTrade> Trades, List<double> R, bool Gross, List<(double Net, double Cost)>? Costs);
}
