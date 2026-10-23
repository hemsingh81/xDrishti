using System.Globalization;

namespace XDrishti.Domain.Selection;

/// <summary>
/// Greedy portfolio fill (doc 10 section 5, spec P7-02, ADR 0084). Walks the ranked candidates best first; a candidate is taken only when every constraint still holds with it added to the open book and the picks so far, so each pick re-checks all constraints.
/// A constraint whose input is not known is <see cref="FilterOutcome.Unavailable"/> and blocks like a fail. Pure and deterministic: the walk order is the rank, then symbol, side and cell key (ordinal), whatever order the input arrives in. Inputs are never changed.
/// Shortcuts: in trigger mode <c>MaxEntriesPerDay</c> is not checked (it applies when tickets trigger); in plan mode entries already opened today are not known and not counted. A reserve may share a symbol with a primary (its stand-in); nothing links them, so a promotion needs a fresh check.
/// Reserve: not taken, but admissible on its own against the open book (so a pick that falls away can be replaced, after a fresh check); the first <c>ReserveSize</c> in rank order. Everything else not taken is rejected.
/// </summary>
public static class PortfolioFiller
{
    private sealed record Holding(string Symbol, string Side, string Style, double? Risk, string? Sector, IReadOnlyList<string>? Baskets);

    /// <summary>Runs the fill.</summary>
    /// <param name="candidates">Ranked eligible candidates with their portfolio facts.</param>
    /// <param name="book">The open positions; null when the book is not known, in which case nothing is taken.</param>
    /// <param name="correlations">Pair correlations; null when not known (any pair that is needed is then unavailable).</param>
    /// <param name="constraints">The limits; must have no problems.</param>
    public static FillResult Fill(IEnumerable<FillCandidate> candidates, IReadOnlyList<BookPosition>? book, CorrelationMatrix? correlations, FillConstraints constraints)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(constraints);
        var problems = constraints.Problems();
        if (problems.Count > 0)
        {
            throw new ArgumentException("Invalid constraints: " + string.Join("; ", problems), nameof(constraints));
        }

        var ordered = candidates
            .OrderBy(c => c.Ranked.Rank)
            .ThenBy(c => c.Symbol, StringComparer.Ordinal)
            .ThenBy(c => c.Side, StringComparer.Ordinal)
            .ThenBy(c => c.Ranked.Merged.Winner.Candidate.Cell.Key, StringComparer.Ordinal)
            .ToList();

        if (book is null)
        {
            var unknown = new[] { new FillCheck(FillCodes.Book, FilterOutcome.Unavailable, null, null, "the open book is not known, so no limit can be checked") };
            return new FillResult([], [], [.. ordered.Select(c => new FillDecision(c, FillStatus.Rejected, unknown))]);
        }

        var bookHoldings = book.Select(b => new Holding(b.Symbol, b.Side, b.Style, FiniteOrZero(b.RiskPct), b.Sector, b.Baskets)).ToList();
        var held = new List<Holding>(bookHoldings);
        var primary = new List<FillDecision>();
        var notTaken = new List<(FillCandidate Candidate, IReadOnlyList<FillCheck> Checks)>();
        foreach (var c in ordered)
        {
            var checks = Evaluate(c, held, primary.Count, correlations, constraints);
            if (checks.All(k => k.Outcome == FilterOutcome.Pass))
            {
                primary.Add(new FillDecision(c, FillStatus.Primary, checks));
                held.Add(ToHolding(c));
            }
            else
            {
                notTaken.Add((c, checks));
            }
        }

        var reserve = new List<FillDecision>();
        var rejected = new List<FillDecision>();
        foreach (var (c, checks) in notTaken)
        {
            // Admissible on its own: against the open book with nothing picked.
            var alone = Evaluate(c, bookHoldings, 0, correlations, constraints);
            if (alone.Any(k => k.Outcome != FilterOutcome.Pass))
            {
                rejected.Add(new FillDecision(c, FillStatus.Rejected, alone));
            }
            else if (reserve.Count < constraints.ReserveSize)
            {
                reserve.Add(new FillDecision(c, FillStatus.Reserve, checks));
            }
            else
            {
                var full = new FillCheck(FillCodes.ReserveFull, FilterOutcome.Fail, reserve.Count, constraints.ReserveSize, "admissible, but the reserve list is full");
                rejected.Add(new FillDecision(c, FillStatus.Rejected, [.. checks, full]));
            }
        }

        return new FillResult(primary, reserve, rejected);
    }

    private static Holding ToHolding(FillCandidate c) => new(c.Symbol, c.Side, c.Style, Finite(c.RiskPct), c.Sector, c.Baskets);

    // A ticket must carry a positive risk (zero would be a free trade nobody can size). An open position may carry 0 (breakeven or locked stop): that is a known figure, not a gap.
    private static double? Finite(double? v) => v is { } x && double.IsFinite(x) && x > 0 ? x : null;

    private static double? FiniteOrZero(double? v) => v is { } x && double.IsFinite(x) && x >= 0 ? x : null;

    private static int Sign(string side) => SideSign.Of(side);

    private static List<FillCheck> Evaluate(FillCandidate c, List<Holding> held, int picked, CorrelationMatrix? correlations, FillConstraints k)
    {
        var checks = new List<FillCheck>();
        if (k.CountEntriesOn == EntriesCountMode.Plan)
        {
            checks.Add(Limit(FillCodes.EntriesPerDay, picked + 1, k.MaxEntriesPerDay, "entries on the plan"));
        }
        else
        {
            checks.Add(Limit(FillCodes.PlanItems, picked + 1, k.MaxPlanItems, "tickets on the plan"));
        }

        checks.Add(Limit(FillCodes.OpenPositions, held.Count + 1, k.MaxOpenPositions, "open positions"));
        checks.Add(OnePerSymbol(c, held));
        if (k.SkipSellOnHeldLongTerm && c.Side == "SELL")
        {
            checks.Add(HeldLongTerm(c));
        }

        var risk = Finite(c.RiskPct);
        checks.Add(PerTrade(risk, k));
        checks.Add(TotalRisk(risk, held, k));
        checks.Add(Net(c, risk, held, k));
        checks.Add(Sector(c, held, k));
        checks.Add(Mix(c, held, k));
        if (k.BasketLimits.Count > 0)
        {
            checks.Add(Basket(c, held, k));
        }

        checks.Add(Correlation(c, held, correlations, k));
        return checks;
    }

    // Symbols, sectors and baskets compare ignoring case and surrounding blanks ("Banks" and "banks" are one sector).
    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static FillCheck Limit(string code, int value, int limit, string what) =>
        new(code, value <= limit ? FilterOutcome.Pass : FilterOutcome.Fail, value, limit, string.Create(CultureInfo.InvariantCulture, $"{what} would be {value}, limit {limit}"));

    private static FillCheck Unavailable(string code, string why) => new(code, FilterOutcome.Unavailable, null, null, why);

    private static FillCheck OnePerSymbol(FillCandidate c, List<Holding> held)
    {
        if (string.IsNullOrWhiteSpace(c.Symbol) || c.Symbol == "*")
        {
            return Unavailable(FillCodes.OnePerSymbol, "the candidate has no instrument yet");
        }

        var clash = held.Count(h => Same(h.Symbol, c.Symbol));
        return new FillCheck(FillCodes.OnePerSymbol, clash == 0 ? FilterOutcome.Pass : FilterOutcome.Fail, clash + 1, 1, clash == 0 ? c.Symbol + " is not yet in the book or the plan" : c.Symbol + " is already in the book or the plan");
    }

    private static FillCheck HeldLongTerm(FillCandidate c) => c.HeldLongTerm switch
    {
        null => Unavailable(FillCodes.HeldLongTerm, "not known whether " + c.Symbol + " is held long-term"),
        true => new FillCheck(FillCodes.HeldLongTerm, FilterOutcome.Fail, null, null, "SELL skipped: " + c.Symbol + " is held long-term"),
        false => new FillCheck(FillCodes.HeldLongTerm, FilterOutcome.Pass, null, null, c.Symbol + " is not held long-term"),
    };

    private static FillCheck PerTrade(double? risk, FillConstraints k) => risk is not { } r
        ? Unavailable(FillCodes.PerTradeRisk, "the ticket's risk is not known")
        : new FillCheck(FillCodes.PerTradeRisk, Math.Round(r, 9) <= k.MaxPerTradeRiskPct ? FilterOutcome.Pass : FilterOutcome.Fail, r, k.MaxPerTradeRiskPct, "risk of the trade " + N(r) + "%, limit " + N(k.MaxPerTradeRiskPct) + "%");

    private static FillCheck TotalRisk(double? risk, List<Holding> held, FillConstraints k)
    {
        if (risk is not { } r || held.Any(h => h.Risk is null))
        {
            return Unavailable(FillCodes.TotalRisk, "a risk figure is not known (the ticket or an open position)");
        }

        var total = held.Sum(h => h.Risk!.Value) + r;
        return new FillCheck(FillCodes.TotalRisk, Math.Round(total, 9) <= k.MaxTotalOpenRiskPct ? FilterOutcome.Pass : FilterOutcome.Fail, total, k.MaxTotalOpenRiskPct, "total open risk would be " + N(total) + "%, limit " + N(k.MaxTotalOpenRiskPct) + "%");
    }

    private static FillCheck Net(FillCandidate c, double? risk, List<Holding> held, FillConstraints k)
    {
        if (risk is not { } r || held.Any(h => h.Risk is null) || Sign(c.Side) == 0 || held.Any(h => Sign(h.Side) == 0))
        {
            return Unavailable(FillCodes.NetExposure, "a risk figure or a side is not known");
        }

        var net = held.Sum(h => h.Side == "SELL" ? -h.Risk!.Value : h.Risk!.Value) + (c.Side == "SELL" ? -r : r);
        var buy = c.Side == "BUY";
        var value = buy ? net : -net;
        var limit = buy ? k.MaxNetLongRiskPct : k.MaxNetShortRiskPct;
        return new FillCheck(FillCodes.NetExposure, Math.Round(value, 9) <= limit ? FilterOutcome.Pass : FilterOutcome.Fail, value, limit, (buy ? "net long" : "net short") + " risk would be " + N(value) + "%, limit " + N(limit) + "%");
    }

    private static FillCheck Sector(FillCandidate c, List<Holding> held, FillConstraints k)
    {
        if (string.IsNullOrWhiteSpace(c.Sector))
        {
            return Unavailable(FillCodes.Sector, "the sector of " + c.Symbol + " is not known");
        }

        var count = held.Count(h => Same(h.Sector, c.Sector)) + 1;
        var check = Limit(FillCodes.Sector, count, k.MaxPerSector, "positions in " + c.Sector);
        return check.Outcome == FilterOutcome.Pass && held.Any(h => string.IsNullOrWhiteSpace(h.Sector))
            ? Unavailable(FillCodes.Sector, "the sector of an open position is not known")
            : check;
    }

    private static FillCheck Mix(FillCandidate c, List<Holding> held, FillConstraints k)
    {
        var intraday = c.Style == "intraday";
        if (!intraday && c.Style != "swing")
        {
            return Unavailable(FillCodes.Mix, "the style '" + c.Style + "' is neither intraday nor swing");
        }

        var style = c.Style;
        var count = held.Count(h => h.Style == style) + 1;
        var check = Limit(FillCodes.Mix, count, intraday ? k.MaxIntraday : k.MaxSwing, style + " positions");
        return check.Outcome == FilterOutcome.Pass && held.Any(h => h.Style is not ("intraday" or "swing"))
            ? Unavailable(FillCodes.Mix, "the style of an open position is not known")
            : check;
    }

    private static FillCheck Basket(FillCandidate c, List<Holding> held, FillConstraints k)
    {
        if (c.Baskets is null)
        {
            return Unavailable(FillCodes.Basket, "the baskets of " + c.Symbol + " are not known");
        }

        FillCheck? worst = null;
        foreach (var (basket, max) in k.BasketLimits.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            if (!c.Baskets.Contains(basket, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (held.Any(h => h.Baskets is null))
            {
                return Unavailable(FillCodes.Basket, "the baskets of an open position are not known");
            }

            var count = held.Count(h => h.Baskets!.Contains(basket, StringComparer.OrdinalIgnoreCase)) + 1;
            var check = Limit(FillCodes.Basket, count, max, "positions from " + basket);
            if (check.Outcome != FilterOutcome.Pass)
            {
                return check;
            }

            worst ??= check;
        }

        return worst ?? new FillCheck(FillCodes.Basket, FilterOutcome.Pass, null, null, "in no limited basket");
    }

    // The correlation that matters is the one of the two bets: corr * side(candidate) * side(held). Opposite sides of positively correlated names are a hedge (effective negative); opposite sides of negatively correlated names are one concentrated bet (effective positive).
    private static FillCheck Correlation(FillCandidate c, List<Holding> held, CorrelationMatrix? matrix, FillConstraints k)
    {
        var others = held.Where(h => !Same(h.Symbol, c.Symbol)).ToList();
        if (others.Count == 0)
        {
            return new FillCheck(FillCodes.Correlation, FilterOutcome.Pass, null, k.MaxPairCorrelation, "nothing to correlate with");
        }

        double? highest = null;
        string? missing = null;
        string? at = null;
        foreach (var other in others)
        {
            var raw = matrix?.Get(c.Symbol, other.Symbol);
            var sides = Sign(c.Side) * Sign(other.Side);
            if (raw is null || sides == 0)
            {
                missing ??= other.Symbol;
                continue;
            }

            var value = raw.Value * sides;
            if (highest is null || value > highest)
            {
                highest = value;
                at = other.Symbol;
            }
        }

        if (highest is { } h && Math.Round(h, 9) > k.MaxPairCorrelation)
        {
            return new FillCheck(FillCodes.Correlation, FilterOutcome.Fail, h, k.MaxPairCorrelation, "same-direction correlation with " + at + " is " + N(h) + ", limit " + N(k.MaxPairCorrelation));
        }

        return missing is not null
            ? new FillCheck(FillCodes.Correlation, FilterOutcome.Unavailable, highest, k.MaxPairCorrelation, "correlation of " + c.Symbol + " with " + missing + " (or a side) is not known")
            : new FillCheck(FillCodes.Correlation, FilterOutcome.Pass, highest, k.MaxPairCorrelation, "highest same-direction correlation " + N(highest!.Value) + ", limit " + N(k.MaxPairCorrelation));
    }
}
