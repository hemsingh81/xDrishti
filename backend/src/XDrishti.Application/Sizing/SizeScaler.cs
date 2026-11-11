using System.Globalization;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Application.Sizing;

/// <summary>What the scaling decided for one primary ticket.</summary>
/// <param name="Decision">The filled ticket.</param>
/// <param name="RiskFactor">The factor on the fixed-risk quantity, in (0, 1]: volatility, gate and budget fit together. Never above 1.</param>
/// <param name="PoolCap">The most shares the ticket may be (the capacity cap); null for no cap.</param>
/// <param name="Note">The structured note for the plan item; null when no sizing setting or budget acted (nothing is added to the plan).</param>
/// <param name="Flags">Evidence codes of the inputs that were unavailable (the ticket is sized at the fixed quantity for them).</param>
/// <param name="ReserveReasons">Set when the ticket goes to the reserve: the codes and the numbers of the first limit that failed.</param>
internal sealed record ScaledTicket(FillDecision Decision, double RiskFactor, long? PoolCap, PlanSizingNote? Note, IReadOnlyList<string> Flags, IReadOnlyList<string>? ReserveReasons)
{
    /// <summary>True when the ticket is not taken.</summary>
    public bool IsReserved => ReserveReasons is not null;
}

/// <summary>
/// The one place the down-only size scaling is applied (spec BK-10, criterion 13): after the portfolio fill (so the cluster cap, the sector and the basket counts keep their order), once per primary ticket in plan order. Pure.
/// The factor is <c>min(volatility scale, gate scale)</c> times the budget ratio and every part is at most 1, so the quantity is never larger than the fixed-risk one. An input that is unavailable never scales by a guess: the ticket stays at its
/// fixed quantity and says so. With every setting OFF and no budget nothing is added: the plan is what it was without this slice.
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

    /// <summary>Reserve code: the scaled quantity is below one share for every paper account that could have taken the fixed quantity.</summary>
    public const string ZeroReserveCode = "sizing_zero";

    /// <param name="primaries">The tickets the fill took, in plan order, each with its indicative entry (the close) and stop.</param>
    /// <param name="context">The plan context (the open book and the accounts).</param>
    /// <param name="inputs">The settings and the per-symbol inputs.</param>
    /// <param name="gateScale">The gate's scale, 0.75 in reduced mode, else 1. The tickets' risk already carries it.</param>
    /// <param name="totalRiskLimit">The total open risk limit, percent of capital.</param>
    public static IReadOnlyList<ScaledTicket> Apply(IReadOnlyList<(FillDecision Decision, double? Entry, double? Stop)> primaries, PlanContext context, SizingInputs inputs, double gateScale, double totalRiskLimit)
    {
        ArgumentNullException.ThrowIfNull(primaries);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inputs);
        var settings = inputs.Settings;
        var options = settings.Options;
        var earlier = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ScaledTicket>(primaries.Count);
        foreach (var (decision, entry, stop) in primaries)
        {
            var inp = inputs.Symbols.GetValueOrDefault(decision.Candidate.Symbol.ToUpperInvariant());
            var parts = new List<string>();
            var flags = new List<string>();

            double? volScale = null;
            if (settings.VolTargetEnabled && inp?.Volatility is { } reading)
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

            var factor = SizingFactors.Combine(volScale, gateScale);
            if (volScale is not null)
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
            BudgetFit? fit = null;
            var budgets = inp?.Budgets ?? [];
            if (budgets.Count > 0)
            {
                fit = FitBudgets(decision, budgets, context, earlier, factor, gateScale, totalRiskLimit, options.MinFit, flags, parts);
                if (fit is { Outcome: BudgetFitOutcome.Reserve })
                {
                    result.Add(Reserved(decision, factor, [$"{BudgetFit.ReserveCode}:fail", fit.Text], flags));
                    continue;
                }

                ratio = fit?.Ratio ?? 1.0;
            }

            var final = Math.Min(1.0, factor * ratio);
            if ((volScale is not null || ratio < 1.0) && ScaledToZero(context, entry, stop, final))
            {
                result.Add(Reserved(decision, factor, [$"{ZeroReserveCode}:fail", string.Create(CultureInfo.InvariantCulture, $"scaled size is below one share at factor {final:0.00}")], flags));
                continue;
            }

            PlanSizingNote? note = parts.Count == 0
                ? null
                : new PlanSizingNote(volScale, Math.Min(gateScale, 1.0), final, poolCap, fit?.Room, fit?.UsesPctOfBudget, inputs.Version, $"sizing: {string.Join("; ", parts)}; settings v{inputs.Version.ToString(CultureInfo.InvariantCulture)}");
            result.Add(new ScaledTicket(decision, final, poolCap, note, flags, null));
        }

        return result;
    }

    private static ScaledTicket Reserved(FillDecision decision, double factor, IReadOnlyList<string> reasons, IReadOnlyList<string> flags) => new(decision, factor, null, null, flags, reasons);

    // True when at least one paper account could take the fixed quantity (one share or more) and every such account would be left below one share by the factor.
    private static bool ScaledToZero(PlanContext context, double? entry, double? stop, double factor)
    {
        if (factor >= 1.0 || entry is not { } e || stop is not { } s || !double.IsFinite(e) || !double.IsFinite(s) || e <= 0 || Math.Abs(e - s) <= 0)
        {
            return false;
        }

        var risk = Math.Abs(e - s);
        var counted = 0;
        foreach (var a in context.Accounts.Where(a => a.Mode == TradingMode.Paper && a.Equity is > 0 && a.RiskPerTradePct is > 0 && a.MaxCapitalPerTradePct is > 0))
        {
            var fixedQuantity = (long)Math.Floor(Math.Min(a.Equity!.Value * a.RiskPerTradePct!.Value / 100 / risk, a.Equity.Value * a.MaxCapitalPerTradePct!.Value / 100 / e));
            if (fixedQuantity < 1)
            {
                continue;
            }

            if (SizingFactors.Quantity(fixedQuantity, factor) >= 1)
            {
                return false;
            }

            counted++;
        }

        return counted > 0;
    }

    // The tightest of the candidate's basket budgets. Returns null when no budget could be judged (unavailable inputs: flagged, the ticket stays at its fixed quantity for the budget).
    private static BudgetFit? FitBudgets(FillDecision decision, IReadOnlyList<BasketBudgetRef> budgets, PlanContext context, Dictionary<string, double> earlier, double factor, double gateScale, double totalRiskLimit, double minFit, List<string> flags, List<string> parts)
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
            var fit = BasketRiskBudget.Fit(budget.BudgetPct, totalRiskLimit, open, risk, minFit);
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
