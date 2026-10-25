using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Reporting;

/// <summary>
/// Reads the stored out-of-sample exit choices (spec P8-10 part b, ADR 0114): the strongest "which exit" evidence the system has, stored by the walk-forward of ADR 0054. A figure is a value only when the record has it and at least the minimum
/// number of trades stand behind it; otherwise it is <c>insufficient</c> or <c>unavailable</c> with a reason and no number.
/// </summary>
internal sealed class ListReportExitSelectionsHandler(IReportComparisonSource source) : IQueryHandler<ListReportExitSelectionsQuery, Result<ReportExitSelectionsView>>
{
    internal static readonly string[] Limits =
    [
        "Out of sample means walk-forward folds inside the same replayed history (about two years): a few folds only, no hold-out, and not evidence of an edge.",
        "A stable verdict does not tell an exit that matters from one that does not; read the chance-pass probability beside it.",
        "Nothing here is applied: no stored strategy takes the chosen exit by itself.",
    ];

    private static readonly int MinTrades = new CellStatsOptions().MinTrades;

    public async ValueTask<Result<ReportExitSelectionsView>> HandleAsync(ListReportExitSelectionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var selections = await source.ListExitSelectionsAsync(cancellationToken);
        return Result.Success(new ReportExitSelectionsView(
            [.. selections.OrderByDescending(s => s.CreatedAt).ThenBy(s => s.SelectionKey, StringComparer.Ordinal).Select(View)],
            Limits));
    }

    private static ExitSelectionView View(ReportExitSelectionInfo s) => new(
        s.SelectionKey,
        s.ComparisonKey,
        s.StrategyId,
        s.Timeframe,
        s.Side,
        s.Style,
        s.Regime,
        s.Unit,
        s.DefaultCandidateId,
        s.ChosenCandidateId,
        s.NoChoiceReason,
        s.Stable,
        s.StabilityReason,
        s.UsableFolds,
        s.AgreeingFolds,
        s.PositiveFolds,
        s.ChancePass,
        s.LonePeaks,
        [.. s.Pooled.Select(p => new PooledFigureView(p.Name, p.Trades, Figure(p.MeanR, p.Trades), Figure(p.Score, p.Trades)))],
        [.. s.Folds.Select(f => new SelectionFoldView(
            f.Index,
            f.TestStart,
            f.TestEnd,
            f.Winner,
            f.WinnerTestTrades,
            f.Winner is null ? new MetricView("empty", null, "no candidate qualified in this fold") : Figure(f.WinnerTestScore, f.WinnerTestTrades),
            f.Thin))],
        [.. s.Banner],
        s.CreatedAt);

    private static MetricView Figure(double? number, int trades) =>
        number is not { } value
            ? new MetricView("unavailable", null, "the stored record has no figure here")
            : trades < MinTrades
                ? new MetricView("insufficient", null, string.Create(CultureInfo.InvariantCulture, $"{trades} {(trades == 1 ? "trade" : "trades")}, at least {MinTrades} needed"))
                : new MetricView("value", value, null);
}
