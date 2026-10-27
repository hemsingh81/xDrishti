using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// The auto-insights (doc 15 section 6, ADR 0099) as fixed sentence templates over the cells of a grouping. A sentence is written only when every group it compares has the minimum number of trades (15) and there are at least two to compare; otherwise the insight
/// has a reason state and says why, with no figure. Numbers are read from the cells, nothing is estimated, no language model is involved. When the runs hold the same signals under different exits or styles, a comparison over all of them would count a signal once per
/// label, so the insights other than the exit are not evaluable for such a population.
/// </summary>
internal static class InsightBuilder
{
    private const string VariesReason = "the runs hold the same signals under different exit or style labels, so a sentence over all of them would count a signal once per label; build the report from the runs of one label";

    public static IReadOnlyList<Insight> Build(ReportPopulation population)
    {
        var min = population.Options.InsightMinTrades;
        var labelsVary = population.StyleVaries || population.ExitVaries;
        Compared? Of(IReadOnlyList<ReportDimension> dimensions, string noun) => labelsVary ? null : Compare(population, dimensions, noun, min);

        var strategies = Of([ReportDimension.Strategy], "strategy");
        var insights = new List<Insight>
        {
            Extreme(InsightKind.BestStrategy, "Best strategy", "No strategy stands out", strategies, best: true),
            Extreme(InsightKind.WeakestStrategy, "Weakest strategy", "No strategy stands out", strategies, best: false),
            population.AnyBasketKnown
                ? Gap(InsightKind.BasketGap, "Basket gap", Of([ReportDimension.Basket], "basket"), " Baskets overlap, so these figures are not additive.")
                : Reason(InsightKind.BasketGap, MetricState.Unavailable, "Basket gap: unavailable, no trade records the baskets of its instrument."),
            Gap(InsightKind.SideBias, "Side bias", Of([ReportDimension.Side], "side"), string.Empty),
        };

        if (population.AnyExitKnown)
        {
            List<ReportDimension> exitDimensions = population.StyleVaries ? [ReportDimension.Exit, ReportDimension.Style] : [ReportDimension.Exit];
            insights.Add(Extreme(InsightKind.BestExit, population.StyleVaries ? "Best exit and style" : "Best exit", population.StyleVaries ? "No exit and style stands out" : "No exit stands out", Compare(population, exitDimensions, population.StyleVaries ? "exit and style" : "exit", min), best: true));
        }
        else
        {
            insights.Add(Reason(InsightKind.BestExit, MetricState.Unavailable, "Best exit: unavailable, no run records its exit policy."));
        }

        insights.Add(Gap(InsightKind.Weekday, "Weekday", Of([ReportDimension.Weekday], "weekday"), string.Empty));
        insights.Add(Reason(InsightKind.Regime, MetricState.Unavailable, "Regime: unavailable here, the report engine does not tag trades with a regime yet; see the regime cells."));
        insights.Add(Reason(InsightKind.Calibration, MetricState.NotEvaluable, "Calibration: not evaluable, it needs the predicted win rates of a champion model and a report uses none."));
        return insights.AsReadOnly();
    }

    private static Insight Extreme(InsightKind kind, string title, string tiedTitle, Compared? compared, bool best)
    {
        if (compared is null)
        {
            return Reason(kind, MetricState.NotEvaluable, $"{title}: not evaluable, {VariesReason}.");
        }

        if (compared.Reason is not null)
        {
            return Reason(kind, compared.State, $"{title}: {compared.Reason}");
        }

        if (compared.Tied)
        {
            return Said(kind, $"{tiedTitle}: the {ReportText.Count(compared.Count)} compared each have {ReportText.R(compared.Best!.ExpectancyR.Number!.Value)} per trade.", compared.Best, compared.Worst);
        }

        var cell = best ? compared.Best! : compared.Worst!;
        return Said(kind, $"{title}: {Describe(cell, compared.ThinBelow)}.", cell);
    }

    private static Insight Gap(InsightKind kind, string title, Compared? compared, string suffix)
    {
        if (compared is null)
        {
            return Reason(kind, MetricState.NotEvaluable, $"{title}: not evaluable, {VariesReason}.");
        }

        if (compared.Reason is not null)
        {
            return Reason(kind, compared.State, $"{title}: {compared.Reason}");
        }

        if (compared.Tied)
        {
            return Said(kind, $"{title}: no difference, the {ReportText.Count(compared.Count)} compared each have {ReportText.R(compared.Best!.ExpectancyR.Number!.Value)} per trade.{suffix}", compared.Best, compared.Worst);
        }

        var gap = compared.Best!.ExpectancyR.Number!.Value - compared.Worst!.ExpectancyR.Number!.Value;
        return Said(kind, $"{title}: {Describe(compared.Best, compared.ThinBelow)} against {Describe(compared.Worst, compared.ThinBelow)}, a gap of {ReportText.Plain(gap)} R.{suffix}", compared.Best, compared.Worst);
    }

    private static Insight Reason(InsightKind kind, MetricState state, string text) => new(kind, state, text, new ValueList<string>([]));

    private static Insight Said(InsightKind kind, string text, params ReportGroup?[] cells) =>
        new(kind, MetricState.Value, text, new ValueList<string>(cells.Where(c => c is not null).Select(c => c!.Key.Text).Distinct(StringComparer.Ordinal)));

    private static string Describe(ReportGroup g, int thinBelow) =>
        $"{g.Key.Label} {ReportText.R(g.ExpectancyR.Number!.Value)} per trade over {ReportText.Count(g.N)} trades (win rate {ReportText.Percent(g.WinRate.Number!.Value)}{(g.N < thinBelow ? ", in-sample, thin sample" : string.Empty)})";

    private static Compared Compare(ReportPopulation population, IReadOnlyList<ReportDimension> dimensions, string noun, int min)
    {
        var grouped = population.GroupAtMinimum(dimensions, min);
        if (grouped.IsFailure)
        {
            return new Compared(MetricState.NotEvaluable, "not evaluable, " + grouped.Error.Message, null, null, 0, false);
        }

        var recorded = grouped.Value.Groups.Where(g => g.Key.Parts.All(p => p.Value is not null)).ToList();
        var qualified = recorded.Where(g => g.N >= min && g.ExpectancyR.Number is not null && g.WinRate.Number is not null).ToList();
        var minText = ReportText.Count(min);
        if (recorded.Count == 0)
        {
            return new Compared(MetricState.Empty, $"nothing to compare, the runs hold no {noun} with a recorded value.", null, null, 0, false);
        }

        if (recorded.Count == 1)
        {
            return new Compared(MetricState.NotEvaluable, $"not evaluable, only one {noun} ({recorded[0].Key.Label}) is in the runs, so none is compared.", null, null, 0, false);
        }

        if (qualified.Count == 0)
        {
            return new Compared(MetricState.Insufficient, $"no group has {minText} trades yet, so no {noun} is compared.", null, null, 0, false);
        }

        if (qualified.Count == 1)
        {
            return new Compared(MetricState.Insufficient, $"only {qualified[0].Key.Label} has {minText} trades; there is nothing to compare it with.", null, null, 0, false);
        }

        var best = qualified.OrderByDescending(g => g.ExpectancyR.Number!.Value).ThenByDescending(g => g.N).ThenBy(g => g.Key.Text, StringComparer.Ordinal).First();
        var worst = qualified.OrderBy(g => g.ExpectancyR.Number!.Value).ThenByDescending(g => g.N).ThenBy(g => g.Key.Text, StringComparer.Ordinal).First();
        return new Compared(MetricState.Value, null, best, worst, qualified.Count, best.ExpectancyR.Number!.Value == worst.ExpectancyR.Number!.Value, population.Options.Stats.MinTrades);
    }

    private sealed record Compared(MetricState State, string? Reason, ReportGroup? Best, ReportGroup? Worst, int Count, bool Tied, int ThinBelow = 0);
}
