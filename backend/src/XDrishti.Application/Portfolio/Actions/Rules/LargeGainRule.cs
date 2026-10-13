namespace XDrishti.Application.Portfolio.Actions.Rules;

/// <summary><b>Large gain</b>: an open, priced holding whose unrealised result is at or above +<c>GainPct</c> % of its cost is <c>info</c> (a prompt to decide whether to hold or book). A holding without a usable price never fires. Pure.</summary>
internal static class LargeGainRule
{
    public static IReadOnlyList<RuleHit> Evaluate(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var t = context.Thresholds;
        var hits = new List<RuleHit>();
        foreach (var holding in context.Holdings.Where(h => h.IsOpen && h.IsPriced))
        {
            var figures = context.Returns(holding);
            if (figures.PnlPct is not { } pct || pct < t.GainPct)
            {
                continue;
            }

            var numbers = new ActionNumbers(PnlPct: pct, Pnl: figures.Pnl, Invested: figures.Invested, Value: figures.Value, ThresholdPct: t.GainPct);
            hits.Add(ItemFactory.ForHolding(
                ActionRuleNames.LargeGain,
                ActionSeverities.Info,
                holding,
                "Large gain",
                $"Up {ActionText.Percent(pct)} on cost ({ActionText.Rupees(figures.Invested)} invested)",
                numbers,
                pct));
        }

        return hits;
    }
}
