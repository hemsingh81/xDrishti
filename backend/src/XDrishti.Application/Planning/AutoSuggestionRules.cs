using System.Globalization;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The pure rules of the suggestions read model (spec AU-16): the evidence figures as text, which are required, and the reason codes in words. A missing figure reads <see cref="Unavailable"/>; a ticket missing a required figure is never suggested.
/// </summary>
internal static class AutoSuggestionRules
{
    public const string Unavailable = "unavailable";

    public const string NoOrderNotice = "Suggestion only, no order is placed";

    public const string AllRegimes = "all regimes";

    /// <summary>The evidence of an item as text, and the names of the required figures it lacks (empty = complete). Required: n above 0, n_eff, conservative expectancy, regime, data as-of.</summary>
    public static (AutoEvidenceView Figures, IReadOnlyList<string> Missing) Evidence(PlanEvidence e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var missing = new List<string>();
        var regime = CellId.TryParse(e.CellKey) is { } cell ? (cell.Regime == "*" ? AllRegimes : cell.Regime) : Unavailable;
        if (e.Trades <= 0)
        {
            missing.Add("n");
        }

        if (e.NEff is null)
        {
            missing.Add("n_eff");
        }

        if (e.ConservativeExpectancyR is null)
        {
            missing.Add("conservative expectancy");
        }

        if (regime == Unavailable)
        {
            missing.Add("regime");
        }

        if (e.StatsAsOf is null)
        {
            missing.Add("data as-of");
        }

        return (new AutoEvidenceView(
            e.Trades > 0 ? e.Trades.ToString(CultureInfo.InvariantCulture) : Unavailable,
            e.NEff is { } n ? n.ToString("0.#", CultureInfo.InvariantCulture) : Unavailable,
            e.ConservativeExpectancyR is { } r ? r.ToString("0.###", CultureInfo.InvariantCulture) : Unavailable,
            regime,
            e.StatsAsOf is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Unavailable), missing);
    }

    /// <summary>A stored reason code in words; <c>code:fail</c> is a check that failed, <c>code:unavailable</c> one that could not be run (never a pass). An unknown code is shown as it is.</summary>
    public static string ReasonInWords(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var colon = code.IndexOf(':', StringComparison.Ordinal);
        var name = colon < 0 ? code : code[..colon];
        var suffix = colon < 0 ? string.Empty : code[(colon + 1)..];
        var words = Words.TryGetValue(name, out var w) ? w : name.Replace('_', ' ');
        return suffix switch
        {
            "fail" => $"Failed: {words}.",
            "unavailable" => $"Could not be checked (unavailable): {words}.",
            _ => char.ToUpperInvariant(words[0]) + words[1..] + ".",
        };
    }

    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["cell_state"] = "the cell is not in a good enough state",
        ["stats_as_of"] = "the cell statistics were not known before the session",
        ["sample"] = "the cell has too few trades",
        ["win_rate"] = "the shrunk win rate is too low",
        ["expectancy"] = "the conservative expectancy is too low",
        ["reward_risk"] = "the reward to risk is too low",
        ["cost"] = "the cost in R is too high",
        ["liquidity"] = "the average daily turnover is too low",
        ["event"] = "an event excludes the instrument",
        ["fno_ban"] = "the instrument is on the F&O ban list",
        ["surveillance"] = "the instrument is under surveillance",
        ["book"] = "the open book is not known",
        ["plan_items"] = "the plan already holds the most tickets",
        ["entries_per_day"] = "the limit on new entries per day is reached",
        ["open_positions"] = "the limit on open positions is reached",
        ["one_per_symbol"] = "a position in this symbol is already taken",
        ["held_long_term"] = "the instrument is held long term",
        ["per_trade_risk"] = "the risk of the single trade is too high",
        ["total_risk"] = "the total open risk would be too high",
        ["net_exposure"] = "the net exposure would be too high",
        ["sector"] = "the sector limit is reached",
        ["mix"] = "the intraday and swing mix limit is reached",
        ["basket"] = "the basket limit is reached",
        ["correlation"] = "it is too correlated with the book",
        ["reserve_full"] = "admissible, but the reserve list is full",
        ["not_taken"] = "not taken",
        ["merged_duplicate"] = "merged into a better cell for the same instrument",
        ["gate_no_trade"] = "the market gate says no trade",
        ["gate_reduced_cut"] = "the market gate halved the plan",
        ["same_bet_better_ticket"] = "the same bet as a better ticket",
        ["same_bet_open_trade"] = "the same bet as an open trade",
    };

    /// <summary>Why a primary ticket was chosen, in words.</summary>
    public static string ChosenInWords(int rank) =>
        string.Create(CultureInfo.InvariantCulture, $"Passed every eligibility filter and the portfolio fill; ranked {rank} in the plan.");
}
