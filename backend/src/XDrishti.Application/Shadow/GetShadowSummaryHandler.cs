using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Shadow;

/// <summary>
/// The summary of the shadow record (spec CF-08 part B, criterion 10, ADR 0212). Read only: raw n, effective n with the correlation used (and that it is the placeholder), the mean R with a 90% interval, the hit rate, by group, the behavioural count of P10 and the edge count
/// side by side, the pace once ten trades are graded, and the chain head. The figures are the gross R (before charges, from the stored gross figure of each trade) unless every graded trade is net and every charge sheet is confirmed as the owner's (P10-00 D5); the label, the flag and the numbers always agree. Every answer says "modelled fills; no queue, no latency", and with no rows it says so.
/// It reads shadow outcomes (through the read port) for display only: nothing that chooses what to approve may use this handler (an architecture test).
/// </summary>
internal sealed class GetShadowSummaryHandler(
    IShadowSettings settings,
    IShadowResultReader reader,
    IPaperLedgerData ledger,
    IBacktestPaperData paperData,
    ICostConfirmationFact costs) : IQueryHandler<GetShadowSummaryQuery, ShadowSummaryView>
{
    private const int TargetEffectiveN = 60;

    private const int Draws = BandOptions.DefaultDraws;

    private const ulong SeedBase = 0xC0F8_0001UL;

    private static readonly string[] Groups = ["primary", "reserve", "rejected"];

    public async ValueTask<ShadowSummaryView> HandleAsync(GetShadowSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var enabled = await settings.IsEnabledAsync(cancellationToken);

        // shortcut: the rows of every epoch are read (there is no epoch filter yet), while the behavioural count below is the newest epoch's, so the two counts can cover different periods; filter by the edge epoch of the rows when the /paper tab needs one epoch.
        var rows = await reader.ResultsAsync(cancellationToken);
        var chain = await reader.ChainAsync(cancellationToken);
        var behavioural = await BehaviouralAsync(cancellationToken);

        var edgeRows = rows.Where(r => r.CountsForEdge).ToList();
        var graded = edgeRows.Where(r => r.Status == "graded" && r.RSim is not null).ToList();
        var days = edgeRows.GroupBy(r => r.SessionDate).Select(g => new EdgeDay(g.Count(), g.Count(r => r.Status == "graded" && r.RSim is not null)));
        var edge = EdgeCount.Compute(days);

        // The pace counts only sessions that have at least one final grade (graded, not triggered or not gradable): a session whose grading has not run yet is not a slow session, so it must not lower the rate.
        var paceDays = edgeRows.GroupBy(r => r.SessionDate).Where(g => g.Any(r => r.Status is "graded" or "not_triggered" or "not_gradable"))
            .Select(g => new EdgeDay(g.Count(), g.Count(r => r.Status == "graded" && r.RSim is not null)));
        var pace = EdgeCount.Compute(paceDays);
        var eta = pace.EtaSessions(TargetEffectiveN);

        // The figure and the label agree: net R only when every graded trade is net and every sheet is confirmed as the owner's, otherwise the gross R (before charges) of the same trades.
        var costs = await CostsAsync(graded, cancellationToken);
        var figures = graded.Select(r => costs.Gross ? GrossOf(r) : r.RSim).OfType<double>().ToList();
        var byGroup = graded.Where(r => (costs.Gross ? GrossOf(r) : r.RSim) is not null).ToLookup(r => r.TicketGroup, r => (costs.Gross ? GrossOf(r) : r.RSim)!.Value);

        var counts = new ShadowCountsView(
            behavioural, edge.RawN, edge.EffectiveN, rows.Count, edgeRows.Count, edgeRows.Count(r => r.Status == "pending"), edgeRows.Count(r => r.Status == "not_triggered"), edgeRows.Count(r => r.Status == "not_gradable"));
        var edgeView = new ShadowEdgeView(
            edge.RawN, edge.EffectiveN, edge.Rho, edge.RhoIsPlaceholder, edge.RhoIsPlaceholder ? ShadowWording.PlaceholderRho : null, pace.Sessions, TargetEffectiveN, eta,
            EtaText(edge, pace, eta));
        var groups = new List<ShadowGroupView> { Group("all", figures) };
        groups.AddRange(Groups.Select(g => Group(g, [.. byGroup[g]])));

        return new ShadowSummaryView(
            enabled, rows.Count > 0, rows.Count > 0 ? null : NoRows(enabled), ShadowWording.ModelledFills, ShadowWording.SeparateCount, counts, edgeView, groups, costs,
            new ShadowChainView(chain.Sessions, chain.Gaps, chain.LastSession, chain.LastDigest));
    }

    /// <summary>The R before charges of a graded row; a gross-basis row's own R when the stored gross figure is missing, else none.</summary>
    private static double? GrossOf(ShadowResultRow row) => row.RGross ?? (row.RBasis == "gross" ? row.RSim : null);

    private static string NoRows(bool enabled) =>
        enabled
            ? "No shadow row is stored yet: the writer records the candidates of the nightly plan before the open of their session, and the grader grades them after the close."
            : "No shadow row is stored: the switch paper.shadow.enabled is OFF (the default), so nothing is written until the owner turns it on.";

    private static string EtaText(EdgeCount edge, EdgeCount pace, int? eta) =>
        eta is { } sessions
            ? string.Create(CultureInfo.InvariantCulture, $"About {sessions} more session(s) to {TargetEffectiveN} effective trades at the rate seen so far ({pace.EffectiveN:0.0} effective in {pace.Sessions} session(s) with a final grade); a straight-line guess.")
            : string.Create(CultureInfo.InvariantCulture, $"No ETA yet: it needs at least {EdgeCount.EtaMinTrades} graded trades (there are {edge.RawN}).");

    private async ValueTask<int> BehaviouralAsync(CancellationToken cancellationToken)
    {
        var epoch = await ledger.EpochAsync(null, cancellationToken);
        if (epoch is null)
        {
            return 0;
        }

        var data = await paperData.ReadAsync(epoch.Id, cancellationToken);
        return CountedSet.Select(data.Rows, data.OtherEpochs).Counts.Counted;
    }

    private static ShadowGroupView Group(string name, List<double> r)
    {
        var n = r.Count;
        if (n == 0)
        {
            var none = new MetricView("unavailable", null, "no graded shadow trade");
            return new ShadowGroupView(name, 0, none, none, none, none);
        }

        var mean = BacktestBand.PaperMean(r);
        var hit = r.Count(x => x > 0) / (double)n;
        if (n < EdgeCount.EtaMinTrades)
        {
            var few = new MetricView("insufficient", null, string.Create(CultureInfo.InvariantCulture, $"{n} graded trades; at least {EdgeCount.EtaMinTrades} are needed for an interval"));
            var thin = "fewer than " + EdgeCount.EtaMinTrades.ToString(CultureInfo.InvariantCulture) + " graded trades";
            return new ShadowGroupView(name, n, new MetricView("provisional", mean, thin), few, few, new MetricView("provisional", hit, thin));
        }

        var (low, high) = BacktestBand.PaperInterval(r, Draws, SeedBase + (ulong)n);
        return new ShadowGroupView(name, n, new MetricView("value", mean, null), new MetricView("value", low, null), new MetricView("value", high, null), new MetricView("value", hit, null));
    }

    private async ValueTask<ShadowCostView> CostsAsync(List<ShadowResultRow> graded, CancellationToken cancellationToken)
    {
        var sheets = graded.Select(r => r.SheetId).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (graded.Count == 0)
        {
            return new ShadowCostView("none yet: no shadow trade is graded", true, [], false);
        }

        var allNet = graded.All(r => r.RBasis == "net");
        if (!allNet)
        {
            return new ShadowCostView(graded.All(r => r.RBasis == "gross") ? "gross, no charges" : "gross, before charges: some trades carry a charge sheet's charges and the rest are gross; the gross R of each is shown", true, sheets, false);
        }

        if (sheets.Count == 0)
        {
            return new ShadowCostView("gross, before charges: a net grade names no charge sheet, so the gross R is shown", true, sheets, false);
        }

        var confirmed = true;
        foreach (var id in sheets)
        {
            confirmed &= await costs.ConfirmedOnAsync(id, cancellationToken) is not null;
        }

        var list = string.Join(", ", sheets);
        return confirmed
            ? new ShadowCostView("net of the charge sheet " + list + ", confirmed as the owner's", false, sheets, true)
            : new ShadowCostView("gross, before charges: the stored charge sheet " + list + " is not confirmed as the owner's, so the figures are the gross R of each trade and are not called net (P10-00 D5)", true, sheets, false);
    }
}
