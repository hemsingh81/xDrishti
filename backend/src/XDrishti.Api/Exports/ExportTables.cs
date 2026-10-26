using System.Globalization;
using XDrishti.Application.Reporting;
using XDrishti.SimExport.Csv;

namespace XDrishti.Api.Exports;

/// <summary>
/// What each exported table looks like (spec P8-20): the columns, and one function from a row of the screen's own view to its cells. Only the reporting views of <c>XDrishti.Application.Reporting</c> are read here, so nothing about an account can reach a file (an architecture
/// test says so). A figure is three columns: the number (only for <c>value</c> and <c>provisional</c>, otherwise empty), its state word and the reason; a non-value is never a digit.
/// </summary>
internal static class ExportTables
{
    private static readonly (string Id, Func<GroupView, MetricView> Of)[] GroupMetrics =
    [
        ("win_rate", g => g.WinRate), ("avg_win_r", g => g.AvgWinR), ("avg_loss_r", g => g.AvgLossR), ("expectancy_r", g => g.ExpectancyR), ("profit_factor", g => g.ProfitFactor), ("total_r", g => g.TotalR),
        ("max_drawdown_r", g => g.MaxDrawdownR), ("longest_loss_streak", g => g.LongestLossStreak), ("avg_sessions_held", g => g.AvgHoldingSessions), ("shrunk_win_rate", g => g.ShrunkWinRate),
        ("win_rate_lower", g => g.WinRateLower), ("shrunk_expectancy_r", g => g.ShrunkExpectancyR), ("expectancy_lower_r", g => g.ExpectancyLowerR),
    ];

    private static readonly string[] GroupColumnNames = ["group", "n", "wins", .. GroupMetrics.SelectMany(m => Triple(m.Id)), "group_provisional"];

    /// <summary>The columns of <c>report-groups</c>.</summary>
    public static IReadOnlyList<string> GroupsColumns { get; } = GroupColumnNames;

    /// <summary>The columns of <c>report-what-works</c>: the ranking facts, then the group's columns.</summary>
    public static IReadOnlyList<string> WhatWorksColumns { get; } = ["rank", "status", "reason", "lower_bound_positive", .. GroupColumnNames];

    /// <summary>The columns of <c>report-equity</c>.</summary>
    public static IReadOnlyList<string> EquityColumns { get; } = ["group", "n", "curve_state", "curve_reason", "date", "cumulative_r", "drawdown_r", "day_trades"];

    /// <summary>The columns of <c>report-trades</c>.</summary>
    public static IReadOnlyList<string> TradesColumns { get; } = ["run_key", "trade_key", "strategy", "timeframe", "side", "symbol", "baskets", "entry_date", "exit_date", "r", "sessions_held", "style", "exit", "provisional", "ordinal"];

    /// <summary>The columns of a pivot of this figure.</summary>
    public static IReadOnlyList<string> PivotColumns(string metric) => ["row", "column", "n", .. Triple(metric)];

    /// <summary>The cells of a figure: the number, the state, the reason.</summary>
    public static void Metric(List<ExportCell> cells, MetricView metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var hasNumber = metric.State is "value" or "provisional" && metric.Number is { } n && double.IsFinite(n);
        var state = metric.State is "value" or "provisional" && !hasNumber ? "unavailable" : metric.State;
        cells.Add(hasNumber ? ExportCell.OfReal(metric.Number!.Value) : ExportCell.Empty);
        cells.Add(ExportCell.OfText(state));
        cells.Add(ExportCell.OfText(metric.Reason));
    }

    /// <summary>The cells of a group (the columns of <see cref="GroupsColumns"/>).</summary>
    public static void Group(List<ExportCell> cells, GroupView group)
    {
        ArgumentNullException.ThrowIfNull(group);
        cells.Add(ExportCell.OfText(group.Key));
        cells.Add(ExportCell.OfWhole(group.N));
        cells.Add(ExportCell.OfWhole(group.Wins));
        foreach (var (_, of) in GroupMetrics)
        {
            Metric(cells, of(group));
        }

        cells.Add(ExportCell.OfFlag(group.Evidence.Provisional));
    }

    /// <summary>The cells of one ranked or unranked cell of what-works.</summary>
    public static void WhatWorksRow(List<ExportCell> cells, int? rank, GroupView group, bool? lowerBoundPositive, string? reason)
    {
        cells.Add(rank is { } r ? ExportCell.OfWhole(r) : ExportCell.Empty);
        cells.Add(ExportCell.OfText(rank is null ? "not_ranked" : "ranked"));
        cells.Add(ExportCell.OfText(reason));
        cells.Add(lowerBoundPositive is { } p ? ExportCell.OfFlag(p) : ExportCell.Empty);
        Group(cells, group);
    }

    /// <summary>The cells of one pivot cell.</summary>
    public static void PivotRow(List<ExportCell> cells, PivotCellView cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        cells.Add(ExportCell.OfText(cell.Row ?? "not recorded"));
        cells.Add(ExportCell.OfText(cell.Column ?? "not recorded"));
        cells.Add(ExportCell.OfWhole(cell.N));
        Metric(cells, cell.Value);
    }

    /// <summary>The cells of one point of an equity curve, or of a group with no curve (<paramref name="point"/> null).</summary>
    public static void EquityRow(List<ExportCell> cells, EquitySeriesView series, EquityPointView? point)
    {
        ArgumentNullException.ThrowIfNull(series);
        cells.Add(ExportCell.OfText(series.Key));
        cells.Add(ExportCell.OfWhole(series.N));
        cells.Add(ExportCell.OfText(series.TotalR.State));
        cells.Add(ExportCell.OfText(series.TotalR.Reason));
        cells.Add(point is { } p ? ExportCell.OfDate(p.Date) : ExportCell.Empty);
        cells.Add(point is { } c ? ExportCell.OfReal(c.CumulativeR) : ExportCell.Empty);
        cells.Add(point is { } d ? ExportCell.OfReal(d.DrawdownR) : ExportCell.Empty);
        cells.Add(point is { } t ? ExportCell.OfWhole(t.Trades) : ExportCell.Empty);
    }

    /// <summary>The cells of one trade.</summary>
    public static void TradeRow(List<ExportCell> cells, TradeLineView trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        cells.Add(ExportCell.OfText(trade.RunKey));
        cells.Add(ExportCell.OfText(trade.TradeKey));
        cells.Add(ExportCell.OfText(trade.Strategy));
        cells.Add(ExportCell.OfText(trade.Timeframe));
        cells.Add(ExportCell.OfText(trade.Side));
        cells.Add(ExportCell.OfText(trade.Symbol));
        cells.Add(ExportCell.OfText(string.Join(';', trade.BasketIds)));
        cells.Add(ExportCell.OfDate(trade.EntryDate));
        cells.Add(ExportCell.OfDate(trade.ExitDate));
        cells.Add(ExportCell.OfReal(trade.R));
        cells.Add(ExportCell.OfWhole(trade.SessionsHeld));
        cells.Add(ExportCell.OfText(trade.Style));
        cells.Add(ExportCell.OfText(trade.Exit));
        cells.Add(ExportCell.OfFlag(trade.Provisional));
        cells.Add(ExportCell.OfWhole(trade.Ordinal));
    }

    /// <summary>
    /// The evidence block of a file (spec P8-20 criterion 3, P8-00 D1), as lines without the comment marker: the basis (with the charge-sheet confirmation when net), the population rule and size, the as-of date, the runs, what was unavailable and every limit, then the notes
    /// of the table. The row count and the table hash are added by the handler.
    /// </summary>
    public static List<string> Evidence(string kind, ReportEvidenceView e, IEnumerable<string> notes)
    {
        ArgumentNullException.ThrowIfNull(e);
        var lines = new List<string>
        {
            $"xDrishti research export; table {kind}; format csv; research tables from stored simulations only; nothing about accounts; holdings or plans",
            e.Basis == "net" ? $"basis: net R after charges; charge sheet confirmed by the owner: {e.NetBasisConfirmation ?? "confirmation not stated"}" : "basis: gross R; before charges (no charge sheet applied)",
            "population rule: the closed trades of the runs listed below; each counted once; trades still open at the end of the data are never counted; replayed history with modelled fills; not a forecast and not advice",
            string.Create(CultureInfo.InvariantCulture, $"population: n={e.N}; runs={e.RunKeys.Count}; default run set={Word(e.DefaultRunSet)}; provisional run used={Word(e.Provisional)}; duplicate records dropped={e.DuplicateTradesDropped}; open at end left out={e.OpenAtEndLeftOut}"),
            $"date range: first entry {Date(e.FirstDate)}; last exit {Date(e.LastDate)}",
            $"as-of: {Date(e.LastDate)}; the last exit date in the data (the date of the data; not the time of the export)",
            "folds: none (a report table has no out-of-sample folds)",
        };
        lines.AddRange(e.Runs.Select(r => string.Create(CultureInfo.InvariantCulture, $"run: {r.RunKey} strategy {r.StrategyId} universe {r.Universe} window {r.WindowStart:yyyy-MM-dd} to {r.WindowEnd:yyyy-MM-dd} {(r.Costed ? "costed" : "gross")} closed {r.ClosedTrades} open at end {r.OpenAtEnd}")));
        lines.AddRange(e.UnavailableInputs.Count == 0 ? ["unavailable inputs: none"] : e.UnavailableInputs.Select(u => "unavailable input: " + u));
        lines.AddRange(e.Limits.Select(l => "limit: " + l));
        lines.Add("reading the figures: a figure column holds a number only when its _state column says value or provisional; any other state means no number exists and the _reason column says why");
        lines.AddRange(notes.Select(n => "note: " + n));
        return lines;
    }

    private static IEnumerable<string> Triple(string id) => [id, id + "_state", id + "_reason"];

    private static string Word(bool value) => value ? "yes" : "no";

    private static string Date(DateOnly? value) => value is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none";
}
