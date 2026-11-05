using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Shadow;

namespace XDrishti.Application.Shadow;

/// <summary>
/// What the R of the graded shadow trades is net of (spec CF-08 part B, P10-00 D5), shared by the summary and the sequential test so that a figure and its label always agree: net R only when every graded trade is net and every charge sheet is confirmed as the
/// owner's, otherwise the gross R (before charges) of the same trades.
/// </summary>
internal static class ShadowCostBasis
{
    /// <summary>The R before charges of a graded row; a gross-basis row's own R when the stored gross figure is missing, else none.</summary>
    public static double? GrossOf(ShadowResultRow row) => row.RGross ?? (row.RBasis == "gross" ? row.RSim : null);

    /// <summary>The R of a graded row on the basis <paramref name="costs"/> names.</summary>
    public static double? Figure(ShadowResultRow row, ShadowCostView costs) => costs.Gross ? GrossOf(row) : row.RSim;

    public static async ValueTask<ShadowCostView> ReadAsync(List<ShadowResultRow> graded, ICostConfirmationFact confirmation, CancellationToken cancellationToken)
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
            confirmed &= await confirmation.ConfirmedOnAsync(id, cancellationToken) is not null;
        }

        var list = string.Join(", ", sheets);
        return confirmed
            ? new ShadowCostView("net of the charge sheet " + list + ", confirmed as the owner's", false, sheets, true)
            : new ShadowCostView("gross, before charges: the stored charge sheet " + list + " is not confirmed as the owner's, so the figures are the gross R of each trade and are not called net (P10-00 D5)", true, sheets, false);
    }
}
