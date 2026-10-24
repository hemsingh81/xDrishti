namespace XDrishti.Domain.Reporting;

/// <summary>The limits that apply to every report over stored simulated trades (P8-00 "Honest limits"), as fields of the evidence, not page prose. Only facts the engine knows are stated; the length of the history is the evidence's date range.</summary>
internal static class ReportLimits
{
    public static List<string> For(ReportBasis basis, bool provisional, ChargeSheetConfirmation? confirmation)
    {
        List<string> limits =
        [
            "Replayed history with modelled fills, not live trading.",
            "In-sample: the rules were shaped on this same history and no hold-out is kept.",
            basis == ReportBasis.Net
                ? $"R is net of the stored charge sheet: {confirmation?.Text}."
                : "R is gross: charges are not taken off, and no figure here is net.",
            "Basket membership is the one stored with each trade (as of the run); a trade in several baskets appears under each, so basket totals are not additive.",
            "Win rates, mean wins and mean non-wins are shrunk toward their parent group, whose figures include the group's own trades; credible bounds treat trades as independent.",
            "The lower bound of expectancy allows for the uncertainty of the win rate, and for that of the mean win and non-win only through that shrinkage and a minimum number of wins and non-wins; it is not a confidence interval of the mean R.",
            "Looking at many cells and picking the best is a selection effect: the number of trades a cell needs grows with the number of cells compared, and some positive lower bounds are expected by chance alone.",
            "Trades that exit on the same day are ordered by run key and trade key, an arbitrary but fixed order that can change the drawdown and the loss streak.",
        ];
        if (provisional)
        {
            limits.Add("At least one run is marked provisional; figures resting on it are marked provisional.");
        }

        return limits;
    }
}
