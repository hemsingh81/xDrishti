using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Market;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Shadow;

/// <summary>
/// The sequential failure test (spec CF-09, ADR 0213). It assembles the edge series (graded shadow rows that count for edge, in the current paper epoch, ordered by session then trade key) and the P10 behavioural series (the counted trades of the same epoch), hands each
/// to the pure <see cref="SequentialFailureTest"/> with the settings, and states the result. Derived on every request: the failure is latched by reading the whole series, so no state is stored, and an epoch restart (P10-05) starts an empty series. A flag only: it
/// changes no setting, ticket or state, and nothing that chooses what to approve may use it (an architecture test). It reads shadow outcomes through the read port for display only.
/// </summary>
internal sealed class GetSequentialStatusHandler(
    IShadowResultReader reader,
    IPaperLedgerData ledger,
    IBacktestPaperData paperData,
    ICostConfirmationFact confirmation,
    SequentialOptions options) : IQueryHandler<GetSequentialStatusQuery, Result<SequentialStatusView>>
{
    public async ValueTask<Result<SequentialStatusView>> HandleAsync(GetSequentialStatusQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Scope is not (GetSequentialStatusQuery.SystemScope or GetSequentialStatusQuery.StrategyScope))
        {
            return Result.Failure<SequentialStatusView>(PaperErrors.Invalid("scope", "The scope must be `strategy` or `system`."));
        }

        var strategy = query.Scope == GetSequentialStatusQuery.StrategyScope ? query.Strategy?.Trim() : null;
        if (query.Scope == GetSequentialStatusQuery.StrategyScope && string.IsNullOrEmpty(strategy))
        {
            return Result.Failure<SequentialStatusView>(PaperErrors.Invalid("strategy", "The strategy scope needs the strategy id."));
        }

        if (strategy is not null && strategy.Contains('|', StringComparison.Ordinal))
        {
            return Result.Failure<SequentialStatusView>(PaperErrors.Invalid("strategy", "A strategy id holds no `|`."));
        }

        if (query.Scope == GetSequentialStatusQuery.SystemScope && query.Strategy is not null)
        {
            return Result.Failure<SequentialStatusView>(PaperErrors.Invalid("strategy", "The system scope takes no strategy."));
        }

        var prefix = strategy is null ? null : strategy + "|";
        var epoch = await ledger.EpochAsync(null, cancellationToken);
        var rho = EdgeCount.PlaceholderRho; // CF-08 uses the placeholder until CF-01 estimates the same-day correlation; so does this test
        var edge = await EdgeAsync(epoch?.Id, prefix, rho, cancellationToken);
        var behavioural = await BehaviouralAsync(epoch?.Id, prefix, rho, cancellationToken);
        const bool RhoIsPlaceholder = true; // until CF-01 is wired into the edge count
        var statements = SequentialWording.Statements(options, rho, RhoIsPlaceholder);
        var view = new SequentialStatusView(
            query.Scope, strategy, epoch?.Id, edge.HasSeries,
            new SequentialBoundariesView(options.CautionBoundary, options.FailureBoundary, options.OnTrackLevel, options.CautionOdds, options.FailureOdds),
            rho, RhoIsPlaceholder, options.SnapshotHash(), options.KeyText(), edge.View, behavioural, statements, string.Empty);
        return Result.Success(view with { Line = Line(view) });
    }

    private async ValueTask<(bool HasSeries, SequentialSeriesView View)> EdgeAsync(long? epochId, string? prefix, double rho, CancellationToken cancellationToken)
    {
        // shortcut: every shadow row of every epoch is read and filtered in memory; filter by edge_epoch (and trade key) in SQL when the record grows.
        var rows = await reader.ResultsAsync(cancellationToken);
        var scoped = rows.Where(r => r.CountsForEdge && r.EdgeEpoch == epochId && (prefix is null || r.TradeKey.StartsWith(prefix, StringComparison.Ordinal))).ToList();

        // The series is the longest run of complete sessions from the start of the epoch: it stops at the first session with a candidate still without a grade. Grades are insert-only, so a later grade can then only add to the end of the series and never rewrite
        // the path already shown (a failure seen stays seen); what is held back is counted and said.
        var sessions = scoped.GroupBy(r => r.SessionDate).OrderBy(g => g.Key).ToList();
        var firstPending = sessions.Find(g => g.Any(r => r.Status == "pending"));
        var complete = sessions.TakeWhile(g => g != firstPending).SelectMany(g => g).ToList();
        var graded = complete.Where(r => r.Status == "graded" && r.RSim is not null).ToList();
        var heldBack = firstPending is null ? 0 : sessions.SkipWhile(g => g != firstPending).SelectMany(g => g).Count(r => r.Status == "graded" && r.RSim is not null);
        var costs = await ShadowCostBasis.ReadAsync(graded, confirmation, cancellationToken);
        var candidates = scoped.GroupBy(r => r.SessionDate).ToDictionary(g => g.Key, g => g.Count());
        var figures = graded.Select(r => (Row: r, R: ShadowCostBasis.Figure(r, costs))).ToList();
        var ordered = figures
            .Where(x => x.R is { } v && double.IsFinite(v))
            .OrderBy(x => x.Row.SessionDate).ThenBy(x => x.Row.TradeKey, StringComparer.Ordinal)
            .ToList();
        var noFigure = figures.Count - ordered.Count;
        var series = ordered.Select(x => new SequentialTrade(x.R!.Value, candidates[x.Row.SessionDate])).ToList();
        var days = ordered.Select(x => x.Row.SessionDate).ToList();
        var basis = (costs.Gross ? "gross R (before charges)" : "net R (after the confirmed charge sheet)") + ", " + ShadowWording.ModelledFills + "; " + costs.Basis;
        var notes = new List<string>();
        if (scoped.Count == 0)
        {
            notes.Add("No shadow candidate of the current paper epoch counts for edge in this scope: there is no series.");
        }
        else if (series.Count == 0 && heldBack == 0)
        {
            notes.Add("No shadow trade of the current paper epoch is graded yet.");
        }

        if (firstPending is not null)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"The series stops before the session {firstPending.Key:yyyy-MM-dd}, which still has a candidate without a grade; {heldBack} graded trade(s) from that session on are held back until it is graded, so that a late grade can only add to the end and never rewrite a shown path."));
        }

        if (noFigure > 0)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{noFigure} graded trade(s) have no gross figure and are left out of the series; they still count among the day's candidates for the design effect."));
        }

        return (scoped.Count > 0, View(basis, notes.Count == 0 ? null : string.Join(' ', notes), series, days, rho, heldBack, firstPending?.Key));
    }

    private async ValueTask<SequentialSeriesView> BehaviouralAsync(long? epochId, string? prefix, double rho, CancellationToken cancellationToken)
    {
        const string Label = "P10 behavioural series: the owner-approved, armed, primary, measured trades of the epoch, in exit order";
        if (epochId is not { } id)
        {
            return View(Label, "No paper epoch yet.", [], [], rho);
        }

        var data = await paperData.ReadAsync(id, cancellationToken);
        IReadOnlyList<PaperCompareRow> rows = prefix is null ? data.Rows : [.. data.Rows.Where(r => r.TradeKey.StartsWith(prefix, StringComparison.Ordinal))];
        var counted = CountedSet.Select(rows, data.OtherEpochs).Trades.Where(t => double.IsFinite(t.R)).ToList();
        var perDay = counted.GroupBy(t => IndiaTime.Today(t.EntryAt)).ToDictionary(g => g.Key, g => g.Count());
        var series = counted.Select(t => new SequentialTrade(t.R, perDay[IndiaTime.Today(t.EntryAt)])).ToList();
        var days = counted.Select(t => IndiaTime.Today(t.ExitAt)).ToList();
        var basis = Label + "; " + (counted.Count == 0 ? "none counted yet" : counted.All(t => t.RBasis == "net") ? "net R as the P10 count stores it" : "some trades are gross (before charges), as the P10 count stores them");
        return View(basis, counted.Count == 0 ? "No counted paper trade in the epoch yet." : null, series, days, rho);
    }

    private SequentialSeriesView View(string basis, string? note, List<SequentialTrade> series, List<DateOnly> days, double rho, int heldBack = 0, DateOnly? heldFrom = null)
    {
        var result = SequentialFailureTest.Run(series, options, rho);
        var state = result.State switch
        {
            SequentialState.OnTrack => "on_track",
            SequentialState.Caution => "caution",
            SequentialState.Failure => "failure",
            _ => "monitoring",
        };
        return new SequentialSeriesView(
            basis, note, state, SequentialWording.StateText(result.State), result.Sum, result.Trades, result.EffectiveN,
            result.FirstCautionTrade, result.FirstCautionTrade is { } c ? days[c - 1] : null, result.FirstFailureTrade, result.FirstFailureTrade is { } f ? days[f - 1] : null,
            heldBack, heldFrom, new SequentialPathView(result.LowestSum, result.LowestAtTrade, result.SigmaUsed));
    }

    private static string Line(SequentialStatusView v)
    {
        var ci = CultureInfo.InvariantCulture;
        var b = v.Boundaries;
        return string.Create(ci, $"sequential failure test (report only, never a pass; fixed P10 gates decide): edge series {Part(v.Edge)}, boundaries caution {b.Caution:0.00} and failure {b.Failure:0.00}; beside it the P10 behavioural series {Part(v.Behavioural)}; rho {v.Rho:0.00}{(v.RhoIsPlaceholder ? " (placeholder until CF-01 estimates it)" : " (estimated)")}; settings {v.SettingsHash[..12]}");
    }

    private static string Part(SequentialSeriesView s) =>
        (s.TradesCounted == 0
            ? s.State + ", no trade in the series"
            : string.Create(CultureInfo.InvariantCulture, $"{s.State}, sum {s.Sum:0.00}, {s.TradesCounted} trade(s), effective {s.EffectiveN:0.0}"))
        + (s.FirstFailureDate is { } d ? $", failure first reached {d:yyyy-MM-dd}" : string.Empty)
        + (s.HeldBackFromSession is { } stuck ? $", series stops before {stuck:yyyy-MM-dd} (a candidate there has no grade and may never get one); {s.HeldBackTrades} graded trade(s) held back" : string.Empty);
}
