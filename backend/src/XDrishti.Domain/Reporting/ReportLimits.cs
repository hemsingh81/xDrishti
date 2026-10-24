namespace XDrishti.Domain.Reporting;

/// <summary>The limits that apply to every report over stored simulated trades (P8-00 "Honest limits"), as fields of the evidence, not page prose. Only facts the engine knows are stated; the length of the history is the evidence's date range.</summary>
internal static class ReportLimits
{
    public static List<string> For(ReportBasis basis, bool provisional)
    {
        List<string> limits =
        [
            "Replayed history with modelled fills, not live trading.",
            "In-sample: the rules were shaped on this same history and no hold-out is kept.",
            basis == ReportBasis.Net
                ? "R is net of the stored charge sheet, which the owner has confirmed."
                : "R is gross: charges are not taken off, and no figure here is net.",
            "Basket membership is the one stored with each trade (as of the run); a trade in several baskets appears under each, so basket totals are not additive.",
            "Win rates are shrunk toward their parent group and credible bounds treat trades as independent; the lower bound of expectancy reflects the uncertainty of the win rate only.",
            "Looking at many cells and picking the best is a selection effect: the best of many will look good by chance.",
        ];
        if (provisional)
        {
            limits.Add("At least one run is marked provisional; figures resting on it are marked provisional.");
        }

        return limits;
    }
}
