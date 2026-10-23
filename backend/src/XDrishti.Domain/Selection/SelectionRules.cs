using System.Globalization;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Selection;

/// <summary>
/// The limits and weights of selection (doc 10 sections 2 to 4, spec P7-01, ADR 0082). The defaults are the design's where it gives one; the others (turnover floor, weights, margin) are stated assumptions of the spec. Where they will be stored is
/// an open question of P7-00 (Q14); this record is what the pure code needs.
/// </summary>
/// <param name="MinState">The lowest cell state that may produce a candidate (P7-00 Q7: Candidate until paper days exist).</param>
/// <param name="MinWinRate">Least shrunk win rate (posterior mean).</param>
/// <param name="MinExpectancyR">Least conservative expectancy in R.</param>
/// <param name="MinRewardRisk">Least reward to risk before the obstacle.</param>
/// <param name="MinSample">Fewest trades in the cell's statistics.</param>
/// <param name="MaxCostR">Most cost in R.</param>
/// <param name="MinDailyTurnover">Least average daily turnover in rupees.</param>
/// <param name="OptionalFilters">Codes whose unavailable answer the owner has chosen not to hold a candidate back for (it stays a caveat on the ranked candidate). A fail on any filter always rejects. Null or empty (the default): every filter is required, so a candidate with no event, F&amp;O ban or surveillance data is held until the data exists (P15).</param>
/// <param name="ConfluenceWeight">Weight of the confluence count in the rank score.</param>
/// <param name="RegimeWeight">Weight of the regime fit (0 to 1).</param>
/// <param name="CorrelationWeight">Weight of the correlation with the book (subtracted).</param>
/// <param name="EventWeight">Weight of the event risk (subtracted).</param>
/// <param name="MaxConfluence">Most other strategies counted as confluence.</param>
/// <param name="ConflictPolicy">What to do with opposite sides of one symbol.</param>
/// <param name="ConflictMargin">How far in R the stronger side must lead to survive.</param>
public sealed record SelectionRules(
    CellState MinState = CellState.Candidate,
    double MinWinRate = 0.45,
    double MinExpectancyR = 0.10,
    double MinRewardRisk = 1.5,
    int MinSample = 30,
    double MaxCostR = 0.10,
    double MinDailyTurnover = 10_000_000,
    IReadOnlyList<string>? OptionalFilters = null,
    double ConfluenceWeight = 0.05,
    double RegimeWeight = 0.05,
    double CorrelationWeight = 0.10,
    double EventWeight = 0.10,
    int MaxConfluence = 2,
    ConflictPolicy ConflictPolicy = ConflictPolicy.KeepStrongerOrDropBoth,
    double ConflictMargin = 0.05)
{
    /// <summary>The optional filters in force; none by default.</summary>
    public IReadOnlyList<string> Optional => OptionalFilters ?? [];

    /// <summary>The problems of the values; empty when the rules can be used.</summary>
    public IReadOnlyList<string> Problems()
    {
        var p = new List<string>();
        if (MinState is not (CellState.Candidate or CellState.Paper or CellState.Active))
        {
            p.Add("minState must be candidate, paper or active");
        }

        Check(p, nameof(MinWinRate), MinWinRate, 0, 1);
        Check(p, nameof(MinExpectancyR), MinExpectancyR, -10, 10);
        Check(p, nameof(MinRewardRisk), MinRewardRisk, 0, 100);
        Check(p, nameof(MaxCostR), MaxCostR, 0, 10);
        Check(p, nameof(MinDailyTurnover), MinDailyTurnover, 0, 1e15);
        Check(p, nameof(ConfluenceWeight), ConfluenceWeight, 0, 10);
        Check(p, nameof(RegimeWeight), RegimeWeight, 0, 10);
        Check(p, nameof(CorrelationWeight), CorrelationWeight, 0, 10);
        Check(p, nameof(EventWeight), EventWeight, 0, 10);
        Check(p, nameof(ConflictMargin), ConflictMargin, 0, 10);
        if (MinSample is < 1 or > 1_000_000)
        {
            p.Add("minSample must be between 1 and 1000000");
        }

        if (MaxConfluence is < 0 or > 10)
        {
            p.Add("maxConfluence must be between 0 and 10");
        }

        foreach (var code in Optional.Where(c => !FilterCodes.All.Contains(c)))
        {
            p.Add(string.Create(CultureInfo.InvariantCulture, $"'{code}' is not a filter code"));
        }

        return p.AsReadOnly();
    }

    private static void Check(List<string> problems, string name, double value, double min, double max)
    {
        if (!double.IsFinite(value) || value < min || value > max)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"{name} must be a number between {min:G} and {max:G}"));
        }
    }
}
