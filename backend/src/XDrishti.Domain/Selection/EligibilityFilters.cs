using System.Globalization;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Selection;

/// <summary>
/// The eligibility filters of doc 10 section 2 as a pure function (spec P7-01, ADR 0082). Each filter gives pass, fail or unavailable with a stable code and the numbers. Unavailable is never a pass. Statistics count only when their as-of date is before the decision date.
/// The conservative expectancy is <c>pLower * avgWinR + (1 - pLower) * avgLossR</c> with the lower credible bound of the win rate, minus the cost in R when the statistics are gross.
/// </summary>
public static class EligibilityFilters
{
    /// <summary>Runs every filter.</summary>
    public static Eligibility Evaluate(PlanCandidate candidate, SelectionRules rules)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(rules);
        var stats = candidate.Stats;
        var statsBlock = StatsBlock(candidate);
        var expectancy = statsBlock is null ? ConservativeExpectancy(stats!, candidate.CostR) : null;
        var results = new List<FilterResult>
        {
            State(candidate, rules),
            statsBlock is null
                ? new FilterResult(FilterCodes.StatsAsOf, FilterOutcome.Pass, null, null, F($"statistics as of {stats!.AsOf:yyyy-MM-dd}, decision {candidate.DecisionDate:yyyy-MM-dd}"))
                : new FilterResult(FilterCodes.StatsAsOf, FilterOutcome.Unavailable, null, null, statsBlock),
            statsBlock is not null
                ? Unavailable(FilterCodes.Sample, statsBlock)
                : AtLeast(FilterCodes.Sample, stats!.N, rules.MinSample, "trades"),
            statsBlock is not null
                ? Unavailable(FilterCodes.WinRate, statsBlock)
                : AtLeast(FilterCodes.WinRate, Finite(stats!.WinRatePosterior), rules.MinWinRate, "shrunk win rate"),
            statsBlock is not null
                ? Unavailable(FilterCodes.Expectancy, statsBlock)
                : AtLeast(FilterCodes.Expectancy, expectancy, rules.MinExpectancyR, "conservative expectancy R"),
            AtLeast(FilterCodes.RewardRisk, Finite(candidate.PlannedRewardRisk), rules.MinRewardRisk, "reward to risk"),
            AtMost(FilterCodes.Cost, Finite(candidate.CostR), rules.MaxCostR, "cost R"),
            AtLeast(FilterCodes.Liquidity, Finite(candidate.AverageDailyTurnover), rules.MinDailyTurnover, "average daily turnover"),
            Flag(FilterCodes.Event, candidate.ExcludedByEvent, "an event excludes the instrument"),
            Flag(FilterCodes.FnoBan, candidate.OnFnoBan, "on the F&O ban list"),
            Flag(FilterCodes.Surveillance, candidate.UnderSurveillance, "under surveillance"),
        };

        var verdict = results.Any(r => r.Outcome == FilterOutcome.Fail)
            ? EligibilityVerdict.Rejected
            : results.Any(r => r.Outcome == FilterOutcome.Unavailable && !rules.Optional.Contains(r.Code))
                ? EligibilityVerdict.Held
                : EligibilityVerdict.Eligible;
        return new Eligibility(candidate, verdict, results.AsReadOnly(), expectancy);
    }

    /// <summary>The conservative expectancy in R; null when the win or loss average is missing (a cell with zero wins or zero losses has no such average, so its expectancy is unavailable and the cell is held, not passed), or the statistics are gross and the cost is unknown.</summary>
    public static double? ConservativeExpectancy(CellStatsRow stats, double? costR)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (Finite(stats.AvgWinR) is not { } win || Finite(stats.AvgLossR) is not { } loss || !double.IsFinite(stats.WinRateLower))
        {
            return null;
        }

        var gross = (stats.WinRateLower * win) + ((1 - stats.WinRateLower) * loss);
        if (stats.Costed)
        {
            return gross;
        }

        return Finite(costR) is { } c ? gross - c : null;
    }

    private static string? StatsBlock(PlanCandidate c)
    {
        if (c.Stats is null)
        {
            return "no statistics for the cell";
        }

        return c.Stats.AsOf >= c.DecisionDate
            ? F($"statistics as of {c.Stats.AsOf:yyyy-MM-dd} are not before the decision date {c.DecisionDate:yyyy-MM-dd}")
            : null;
    }

    private static FilterResult State(PlanCandidate c, SelectionRules rules)
    {
        if (c.State is not { } state)
        {
            return Unavailable(FilterCodes.CellState, "the cell's state is not known");
        }

        var rank = Rank(state);
        var min = Rank(rules.MinState);
        return new FilterResult(FilterCodes.CellState, rank >= min && rank > 0 ? FilterOutcome.Pass : FilterOutcome.Fail, rank, min, $"state {state.Code()}, minimum {rules.MinState.Code()}");
    }

    private static int Rank(CellState s) => s switch
    {
        CellState.Candidate => 1,
        CellState.Paper => 2,
        CellState.Active => 3,
        _ => 0,
    };

    private static FilterResult AtLeast(string code, double? value, double limit, string what) =>
        value is not { } v
            ? Unavailable(code, F($"{what} is not available"))
            : new FilterResult(code, Math.Round(v, 9) >= limit ? FilterOutcome.Pass : FilterOutcome.Fail, v, limit, F($"{what} {v:0.####}, at least {limit:0.####}"));

    private static FilterResult AtMost(string code, double? value, double limit, string what) =>
        value is not { } v
            ? Unavailable(code, F($"{what} is not available"))
            : new FilterResult(code, Math.Round(v, 9) <= limit ? FilterOutcome.Pass : FilterOutcome.Fail, v, limit, F($"{what} {v:0.####}, at most {limit:0.####}"));

    private static FilterResult Flag(string code, bool? bad, string what) =>
        bad is not { } b
            ? Unavailable(code, "no data")
            : new FilterResult(code, b ? FilterOutcome.Fail : FilterOutcome.Pass, null, null, b ? what : "clear");

    private static FilterResult Unavailable(string code, string detail) => new(code, FilterOutcome.Unavailable, null, null, detail);

    private static double? Finite(double? v) => v is { } x && double.IsFinite(x) ? x : null;

    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
