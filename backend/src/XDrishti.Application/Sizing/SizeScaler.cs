using System.Globalization;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Application.Sizing;

/// <summary>What the scaling decided for one primary ticket.</summary>
/// <param name="Decision">The filled ticket.</param>
/// <param name="RiskFactor">The factor on the fixed-risk quantity, in (0, 1]: volatility, gate and budget fit together. Never above 1.</param>
/// <param name="PoolCap">The most shares the ticket may be (the capacity cap); null for no cap.</param>
/// <param name="Notes">The lines for the ticket's reasons (the computed scale, applied or not applied, and the numbers); empty when nothing was computed.</param>
/// <param name="Flags">Evidence codes of the inputs that were unavailable (the ticket is sized at the fixed quantity for them).</param>
/// <param name="ReserveReasons">Set when the ticket goes to the reserve: the codes and the numbers of the first limit that failed.</param>
internal sealed record ScaledTicket(FillDecision Decision, double RiskFactor, long? PoolCap, IReadOnlyList<string> Notes, IReadOnlyList<string> Flags, IReadOnlyList<string>? ReserveReasons)
{
    /// <summary>True when the ticket is not taken.</summary>
    public bool IsReserved => ReserveReasons is not null;
}

/// <summary>
/// The one place the down-only size scaling is applied (spec BK-10, criterion 13): after the portfolio fill (so the cluster cap, the sector and the basket counts keep their order), once per primary ticket in plan order. Pure.
/// The factor is <c>min(volatility scale, gate scale)</c> times the budget ratio and every part is at most 1, so the quantity is never larger than the fixed-risk one. An input that is unavailable never scales by a guess: the ticket stays at its
/// fixed quantity and says so.
/// </summary>
internal static class SizeScaler
{
    /// <summary>Evidence code: the volatility could not be computed.</summary>
    public const string VolatilityUnavailable = "sizing:volatility_unavailable";

    /// <summary>Evidence code: the 20-session traded value could not be computed.</summary>
    public const string CapacityUnavailable = "sizing:capacity_unavailable";

    /// <summary>Evidence code: the basket's open risk or the candidate's risk is not known.</summary>
    public const string BudgetUnavailable = "sizing:budget_unavailable";

    /// <summary>Reserve code: the capacity cap is below one share.</summary>
    public const string CapacityReserveCode = "capacity_cap";

    /// <param name="primaries">The tickets the fill took, in plan order, each with its indicative entry (the close).</param>
    /// <param name="context">The plan context (the open book).</param>
    /// <param name="inputs">The settings and the per-symbol inputs.</param>
    /// <param name="gateScale">The gate's scale, 0.75 in reduced mode, else 1. The tickets' risk already carries it.</param>
    /// <param name="totalRiskLimit">The total open risk limit, percent of capital.</param>
    public static IReadOnlyList<ScaledTicket> Apply(IReadOnlyList<(FillDecision Decision, double? Entry)> primaries, PlanContext context, SizingInputs inputs, double gateScale, double totalRiskLimit)
    {
        ArgumentNullException.ThrowIfNull(primaries);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inputs);
        var settings = inputs.Settings;
        var options = settings.Options;
        var earlier = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ScaledTicket>(primaries.Count);
        foreach (var (decision, entry) in primaries)
        {
            var symbol = decision.Candidate.Symbol;
            var inp = inputs.Symbols.GetValueOrDefault(symbol.ToUpperInvariant());
            var notes = new List<string>();
            var parts = new List<string>();
            var flags = new List<string>();

            double? volScale = null;
            if (inp?.Volatility is { } reading)
            {
                if (settings.VolTargetEnabled)
                {
                    if (reading is { Status: VolReading.Ok, Scale: { } s })
                    {
                        volScale = s;
                        parts.Add(string.Create(CultureInfo.InvariantCulture, $"volatility {reading.Ratio:0.00}x its usual, scale {s:0.00}"));
                    }
                    else
                    {
                        flags.Add(VolatilityUnavailable);
                        parts.Add("volatility unknown, fixed size");
                    }
                }
                else if (reading is { Status: VolReading.Ok, Scale: { } off } && off < 1.0)
                {
                    parts.Add(string.Create(CultureInfo.InvariantCulture, $"volatility scale {off:0.00} not applied (setting off)"));
                }
            }

            var factor = SizingFactors.Combine(volScale, gateScale);
            if (volScale is not null || gateScale < 1.0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"factor {factor:0.00} (gate {Math.Min(gateScale, 1.0):0.00})"));
            }

            long? poolCap = null;
            if (settings.CapacityEnabled)
            {
                if (CapacityCap.Quantity(inp?.Adv20, entry, options.CapacityPct) is { } cap)
                {
                    if (cap < 1)
                    {
                        result.Add(Reserved(decision, factor, [$"{CapacityReserveCode}:fail", string.Create(CultureInfo.InvariantCulture, $"capacity: {options.CapacityPct * 100:0.##} % of 20-day traded value is below one share")], flags));
                        continue;
                    }

                    poolCap = cap;
                    parts.Add(string.Create(CultureInfo.InvariantCulture, $"capped at {options.CapacityPct * 100:0.##} % of 20-day traded value ({cap} shares)"));
                }
                else
                {
                    flags.Add(CapacityUnavailable);
                    parts.Add("capacity unknown");
                }
            }

            var ratio = 1.0;
            var budgets = inp?.Budgets ?? [];
            if (budgets.Count > 0)
            {
                var fit = FitBudgets(decision, budgets, context, earlier, factor, gateScale, totalRiskLimit, options.Floor, flags, parts);
                if (fit is { Outcome: BudgetFitOutcome.Reserve })
                {
                    result.Add(Reserved(decision, factor, [$"{BudgetFit.ReserveCode}:fail", fit.Text], flags));
                    continue;
                }

                ratio = fit?.Ratio ?? 1.0;
            }

            if (parts.Count > 0)
            {
                notes.Add($"sizing: {string.Join("; ", parts)}; settings v{inputs.Version.ToString(CultureInfo.InvariantCulture)}");
            }

            result.Add(new ScaledTicket(decision, Math.Min(1.0, factor * ratio), poolCap, notes, flags, null));
        }

        return result;
    }

    private static ScaledTicket Reserved(FillDecision decision, double factor, IReadOnlyList<string> reasons, IReadOnlyList<string> flags) => new(decision, factor, null, [], flags, reasons);

    // The tightest of the candidate's basket budgets. Returns null when no budget could be judged (unavailable inputs: flagged, the ticket stays at its fixed quantity for the budget).
    private static BudgetFit? FitBudgets(FillDecision decision, IReadOnlyList<BasketBudgetRef> budgets, PlanContext context, Dictionary<string, double> earlier, double factor, double gateScale, double totalRiskLimit, double floor, List<string> flags, List<string> parts)
    {
        if (decision.Candidate.RiskPct is not { } gated || !double.IsFinite(gated) || gated <= 0 || context.Book is not { } book || book.Any(b => b.Baskets is null))
        {
            flags.Add(BudgetUnavailable);
            parts.Add("basket budget unknown, fixed size");
            return null;
        }

        // The ticket's risk carries the gate's scale already; the budget is judged on the risk the scaled ticket would take.
        var unscaled = gateScale > 0 ? gated / Math.Min(gateScale, 1.0) : gated;
        var risk = unscaled * factor;
        BudgetFit? worst = null;
        foreach (var budget in budgets)
        {
            var open = book.Where(b => b.Baskets!.Contains(budget.BasketName, StringComparer.OrdinalIgnoreCase)).Sum(b => b.RiskPct) + earlier.GetValueOrDefault(budget.BasketName);
            var fit = BasketRiskBudget.Fit(budget.BudgetPct, totalRiskLimit, open, risk, floor);
            if (worst is null || fit.Ratio < worst.Ratio)
            {
                worst = fit;
            }
        }

        if (worst is { Outcome: not BudgetFitOutcome.Reserve })
        {
            parts.Add(worst.Text);
            foreach (var budget in budgets)
            {
                earlier[budget.BasketName] = earlier.GetValueOrDefault(budget.BasketName) + (risk * worst.Ratio);
            }
        }

        return worst;
    }
}
