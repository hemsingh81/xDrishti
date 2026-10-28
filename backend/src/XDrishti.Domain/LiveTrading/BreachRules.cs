namespace XDrishti.Domain.LiveTrading;

/// <summary>A breach rule that fires (spec P11-04): its code, what it does and why in words. B1 to B7.</summary>
public sealed record Breach(string Code, string Action, string Reason)
{
    public const string Hold = "hold";
    public const string StepDown = "step_down";
}

/// <summary>
/// The automatic breach rules B1 to B7 (spec P11-04, ADR 0151), pure. A hold blocks raising only; a step-down lowers the step on the next plan. A fact that is not known fires nothing (unknown is not a breach; the same unknown keeps the gates
/// from passing). The rules read the evidence of the current step.
/// </summary>
public static class BreachRules
{
    /// <summary>B1 to B7 over the evidence. B1 after_halt fill: hold, a second within 10 sessions: step down. B2 two over-allocated entries and B3 two unprotected intraday entries in 10 sessions: hold. B4 n at least 15 and the lower bound of mean slippage above the limit: hold. B5 two of the last 5 sessions gap or not run: hold. B6 drawdown above the weekly loss limit in R: step down. B7 a second weekly halt within 8 weeks: step down.</summary>
    public static IReadOnlyList<Breach> Evaluate(RampEvidence e, RampThresholds t, double weeklyLossLimitR)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(t);
        var found = new List<Breach>();
        if (e.AfterHaltLast10 >= 2)
        {
            found.Add(new("B1", Breach.StepDown, $"{e.AfterHaltLast10} entries filled after a halt in 10 sessions"));
        }
        else if (e.AfterHaltLast10 == 1)
        {
            found.Add(new("B1", Breach.Hold, "an entry was filled after a halt was open"));
        }

        if (e.OverAllocationLast10 >= 2)
        {
            found.Add(new("B2", Breach.Hold, $"{e.OverAllocationLast10} over-allocated entries in 10 sessions"));
        }

        if (e.UnprotectedLast10 >= 2)
        {
            found.Add(new("B3", Breach.Hold, $"{e.UnprotectedLast10} unprotected intraday entries in 10 sessions"));
        }

        if (e.SlippageR.Count >= t.BreachSlippageMinN && OneSidedT.Bounds(e.SlippageR) is { } b && b.Lower > t.MaxSlippageUpperR)
        {
            found.Add(new("B4", Breach.Hold, $"slippage is worse than {t.MaxSlippageUpperR:0.00} R even at the lower bound ({b.Lower:0.000} R, n {e.SlippageR.Count})"));
        }

        if (e.BadCaptureLast5 >= 2)
        {
            found.Add(new("B5", Breach.Hold, $"{e.BadCaptureLast5} of the last 5 sessions had a capture gap or did not run"));
        }

        if (e.DrawdownR is { } d && d > weeklyLossLimitR)
        {
            found.Add(new("B6", Breach.StepDown, $"drawdown {d:0.00} R at this step is above the weekly limit of {weeklyLossLimitR:0.##} R"));
        }

        if (e.WeeklyHaltsLast8Weeks >= 2)
        {
            found.Add(new("B7", Breach.StepDown, $"{e.WeeklyHaltsLast8Weeks} weekly halts within 8 weeks"));
        }

        return found;
    }

    /// <summary>
    /// What still blocks clearing a hold. The event rules (B1 to B3) count over the cooldown only, so they need <see cref="RampThresholds.HoldCooldownSessions"/> sessions without a new one; B4 and B5 stay while their condition does. A step-down rule
    /// is not a hold condition (it is lifted by a new step, gates applying).
    /// </summary>
    public static IReadOnlyList<Breach> HoldConditions(RampEvidence e, RampThresholds t)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(t);
        var found = new List<Breach>();
        if (e.AfterHaltLast5 > 0)
        {
            found.Add(new("B1", Breach.Hold, $"an entry was filled after a halt within the last {t.HoldCooldownSessions} sessions"));
        }

        if (e.OverAllocationLast5 > 0)
        {
            found.Add(new("B2", Breach.Hold, $"an over-allocated entry within the last {t.HoldCooldownSessions} sessions"));
        }

        if (e.UnprotectedLast5 > 0)
        {
            found.Add(new("B3", Breach.Hold, $"an unprotected intraday entry within the last {t.HoldCooldownSessions} sessions"));
        }

        found.AddRange(Evaluate(e, t, double.PositiveInfinity).Where(x => x.Code is "B4" or "B5"));
        return found;
    }
}
