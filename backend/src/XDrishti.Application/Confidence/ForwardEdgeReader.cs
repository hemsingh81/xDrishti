using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Readiness;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Confidence;

/// <summary>
/// Reads the forward edge series of a strategy for the scorecard through the one shared series rule (<see cref="EdgeSeries"/>, the same that the sequential test and the decay report read), so that the scorecard cannot read a different series. The effective n uses the same
/// placeholder same-day correlation as CF-08 and CF-09 until CF-01 estimates it. The shadow rows are read once per instance (a scope), however many strategies ask. Read only.
/// </summary>
internal sealed class ForwardEdgeReader(IShadowResultReader reader, IPaperLedgerData ledger, ICostConfirmationFact confirmation)
{
    private IReadOnlyList<ShadowResultRow>? _rows;

    private long? _epoch;

    private bool _epochRead;

    private async ValueTask<EdgeSelection> SelectAsync(string strategy, CancellationToken cancellationToken)
    {
        if (!_epochRead)
        {
            _epoch = (await ledger.EpochAsync(null, cancellationToken))?.Id;
            _epochRead = true;
        }

        // shortcut: every shadow row of every epoch is read and filtered in memory, as the sequential test and the decay report do; filter in SQL when the record grows.
        _rows ??= await reader.ResultsAsync(cancellationToken);
        return EdgeSeries.Select(_rows, _epoch, strategy + "|");
    }

    /// <summary>
    /// The graded forward trades of the strategy in the current epoch (the same selection as the edge series, before any figure is asked for): the symbol and the session of each, for the liquidity tier of the hard fail "evidence only from tier C" (ADR 0224). The shadow rows
    /// cover every ticket of the paper record (one key for both), so these are the shadow and paper trades. A key without a symbol gives a trade with a null symbol, whose tier is then unavailable.
    /// </summary>
    public async ValueTask<IReadOnlyList<ForwardTrade>> GradedTradesAsync(string strategy, CancellationToken cancellationToken)
    {
        var selection = await SelectAsync(strategy, cancellationToken);
        var prefixLength = strategy.Length + 1;
        return
        [
            .. selection.Graded.Select(r =>
            {
                var rest = r.TradeKey.Length > prefixLength ? r.TradeKey[prefixLength..] : string.Empty;
                var end = rest.IndexOf('|', StringComparison.Ordinal);
                var symbol = end > 0 ? rest[..end] : null;
                return new ForwardTrade(symbol, r.SessionDate);
            }),
        ];
    }

    public async ValueTask<ForwardEdge> ReadAsync(string strategy, CancellationToken cancellationToken)
    {
        var selection = await SelectAsync(strategy, cancellationToken);
        var costs = await ShadowCostBasis.ReadAsync(selection.Graded, confirmation, cancellationToken);
        var figures = EdgeSeries.Figures(selection, costs);
        var trades = figures.Trades;
        var basis = EdgeSeries.Basis(costs);
        var nEff = trades.Sum(t => 1.0 / EdgeCount.DesignEffect(t.DayCandidates, EdgeCount.PlaceholderRho));
        if (costs.Gross || trades.Count < 2)
        {
            return new ForwardEdge(trades.Count, nEff, null, null, null, costs.Gross, basis);
        }

        var rs = trades.Select(t => t.R).ToList();
        var mean = rs.Average();
        var sd = Math.Sqrt(rs.Sum(r => (r - mean) * (r - mean)) / (rs.Count - 1));
        var months = trades.GroupBy(t => (t.Row.SessionDate.Year, t.Row.SessionDate.Month)).Where(g => g.Count() >= ScoreAnchors.MonthMinTrades).ToList();
        double? share = months.Count == 0 ? null : (double)months.Count(g => g.Average(t => t.R) > 0) / months.Count;
        return new ForwardEdge(trades.Count, nEff, mean, sd, share, false, basis);
    }
}
