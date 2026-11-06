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

    public async ValueTask<ForwardEdge> ReadAsync(string strategy, CancellationToken cancellationToken)
    {
        if (!_epochRead)
        {
            _epoch = (await ledger.EpochAsync(null, cancellationToken))?.Id;
            _epochRead = true;
        }

        // shortcut: every shadow row of every epoch is read and filtered in memory, as the sequential test and the decay report do; filter in SQL when the record grows.
        _rows ??= await reader.ResultsAsync(cancellationToken);
        var selection = EdgeSeries.Select(_rows, _epoch, strategy + "|");
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
