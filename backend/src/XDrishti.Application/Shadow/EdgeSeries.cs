using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Domain.Market;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Shadow;

/// <summary>The graded shadow trades of one scope as the edge series reads them (spec CF-09 and CF-12).</summary>
/// <param name="ScopedCount">Shadow candidates of the epoch that count for edge in the scope (graded or not).</param>
/// <param name="Graded">The graded trades of the complete sessions from the start of the epoch, not yet ordered.</param>
/// <param name="HeldBack">Graded trades after the first session that still has a candidate without a grade.</param>
/// <param name="FirstPending">That session, or null when every session is complete.</param>
/// <param name="CandidatesBySession">How many candidates each session had.</param>
internal sealed record EdgeSelection(int ScopedCount, List<ShadowResultRow> Graded, int HeldBack, DateOnly? FirstPending, Dictionary<DateOnly, int> CandidatesBySession);

/// <summary>One graded trade of the edge series: its row, the R on the cost basis in force, and how many candidates its session had (for the design effect).</summary>
/// <param name="Row">The shadow row.</param>
/// <param name="R">The finite R on the basis in force.</param>
/// <param name="DayCandidates">Candidates of the row's session.</param>
internal sealed record EdgeFigure(ShadowResultRow Row, double R, int DayCandidates);

/// <summary>The edge series in order, and how many graded trades had no figure on the basis in force.</summary>
/// <param name="Trades">The trades with a finite figure, by session then trade key.</param>
/// <param name="NoFigure">Graded trades left out for want of a figure.</param>
internal sealed record EdgeFigures(IReadOnlyList<EdgeFigure> Trades, int NoFigure);

/// <summary>
/// The one place that decides which shadow rows make the edge series (shared by the sequential failure test of CF-09 and the decay report of CF-12, so they cannot read different series): the rows that count for edge in the epoch and scope, cut at the first
/// session with a candidate still without a grade. Grades are insert-only, so a later grade can then only add to the end of the series and never rewrite the path already shown; what is held back is counted. Pure: it takes the rows, it reads nothing.
/// </summary>
internal static class EdgeSeries
{
    public static EdgeSelection Select(IReadOnlyList<ShadowResultRow> rows, long? epochId, string? prefix)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var scoped = rows.Where(r => r.CountsForEdge && r.InSet && r.EdgeEpoch == epochId && (prefix is null || r.TradeKey.StartsWith(prefix, StringComparison.Ordinal))).ToList();
        var sessions = scoped.GroupBy(r => r.SessionDate).OrderBy(g => g.Key).ToList();
        var firstPending = sessions.Find(g => g.Any(r => r.Status == "pending"));
        var complete = sessions.TakeWhile(g => g != firstPending).SelectMany(g => g).ToList();
        var graded = complete.Where(r => r.Status == "graded" && r.RSim is not null).ToList();
        var heldBack = firstPending is null ? 0 : sessions.SkipWhile(g => g != firstPending).SelectMany(g => g).Count(r => r.Status == "graded" && r.RSim is not null);
        return new EdgeSelection(scoped.Count, graded, heldBack, firstPending?.Key, scoped.GroupBy(r => r.SessionDate).ToDictionary(g => g.Key, g => g.Count()));
    }

    /// <summary>The graded trades on the cost basis <paramref name="costs"/> names: a trade without a finite figure is left out and counted, the rest are ordered by session then trade key (ordinal). Shared by the sequential test and the decay report so that they read one series.</summary>
    public static EdgeFigures Figures(EdgeSelection selection, ShadowCostView costs)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(costs);
        var figures = selection.Graded.Select(r => (Row: r, R: ShadowCostBasis.Figure(r, costs))).ToList();
        var ordered = figures
            .Where(x => x.R is { } v && double.IsFinite(v))
            .OrderBy(x => x.Row.SessionDate).ThenBy(x => x.Row.TradeKey, StringComparer.Ordinal)
            .Select(x => new EdgeFigure(x.Row, x.R!.Value, selection.CandidatesBySession[x.Row.SessionDate]))
            .ToList();
        return new EdgeFigures(ordered, figures.Count - ordered.Count);
    }

    /// <summary>What the R of the edge series is, in words (gross or net, whose fills, and the cost sheet's state).</summary>
    public static string Basis(ShadowCostView costs)
    {
        ArgumentNullException.ThrowIfNull(costs);
        return (costs.Gross ? "gross R (before charges)" : "net R (after the confirmed charge sheet)") + ", " + ShadowWording.ModelledFills + "; " + costs.Basis;
    }

    /// <summary>The instant a session is taken to be known: its date at 00:00 India time, as Unix seconds (the order of sessions, not a market time).</summary>
    public static long SessionInstant(DateOnly session) => new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUnixTimeSeconds();
}
