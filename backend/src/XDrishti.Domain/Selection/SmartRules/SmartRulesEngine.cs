using System.Globalization;

namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>
/// Applies the smart rules to a ranked list, in this order (doc 09 section 14): market gate, correlation-aware selection, equity-curve throttle.
/// Entry window and meta-labelling are not applied here (the window needs the owner's acceptance, ADR 0069; the meta size multiplier is advisory, ADR 0075).
/// Pure and deterministic; the list is re-read in rank order, so input order does not matter. A rule whose input is missing says so in a flag and never counts as a pass.
/// </summary>
public static class SmartRulesEngine
{
    /// <summary>Applies the rules to <paramref name="ranked"/> for <paramref name="decisionDate"/>.</summary>
    public static SmartPlan Apply(IEnumerable<RankedCandidate> ranked, DateOnly decisionDate, SmartInputs inputs, SmartRulesOptions options)
    {
        ArgumentNullException.ThrowIfNull(ranked);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Problems() is { Count: > 0 } problems)
        {
            throw new ArgumentException("Invalid smart rule options: " + string.Join("; ", problems), nameof(options));
        }

        var list = ranked.OrderBy(r => r.Rank).ThenBy(r => r.Merged.Winner.Candidate.Symbol, StringComparer.Ordinal).ToList();
        var reserve = new List<ReservedCandidate>();
        var flags = new List<PlanFlag>();

        // 1. Market gate.
        var gate = MarketGate.Evaluate(inputs.Gate, decisionDate, options);
        var afterGate = list;
        var gateRiskMultiplier = 1.0;
        switch (gate.Mode)
        {
            case GateMode.NoTrade:
                reserve.AddRange(list.Select(r => new ReservedCandidate(r, SmartRuleCodes.ReserveGateNoTrade, "market gate: no-trade (" + string.Join("; ", gate.Triggers) + ")")));
                afterGate = [];
                break;
            case GateMode.Reduced:
                var keep = (int)Math.Ceiling(list.Count * options.ReducedItemsFactor);
                afterGate = [.. list.Take(keep)];
                reserve.AddRange(list.Skip(keep).Select(r => new ReservedCandidate(r, SmartRuleCodes.ReserveGateReduced, "market gate: reduced (" + string.Join("; ", gate.Triggers) + "), only the best " + keep.ToString(CultureInfo.InvariantCulture) + " kept")));
                gateRiskMultiplier = options.ReducedRiskMultiplier;
                break;
            case GateMode.Unavailable:
                flags.Add(new PlanFlag(SmartRuleCodes.GateUnavailable, "market gate unavailable (" + string.Join(", ", gate.MissingInputs) + " not known): the plan is NOT treated as a normal day; judge the market yourself"));
                break;
            case GateMode.Normal:
                break;
        }

        if (gate.Status == RuleStatus.Applied && gate.MissingInputs.Count > 0)
        {
            flags.Add(new PlanFlag(SmartRuleCodes.GateUnavailable, "market gate partly unavailable (" + string.Join(", ", gate.MissingInputs) + " not known); a limit that is known tripped"));
        }

        AddHalfFlag(flags, "market_gate", list.Count, list.Count - afterGate.Count, options);

        // 2. Correlation-aware selection.
        var (primary, status, unavailablePairs) = ApplyCorrelation(afterGate, inputs, options, reserve);
        if (status == RuleStatus.Unavailable)
        {
            flags.Add(new PlanFlag(SmartRuleCodes.CorrelationUnavailable, "correlation check unavailable (no return correlations): same-bet tickets are not screened"));
        }
        else if (unavailablePairs > 0)
        {
            flags.Add(new PlanFlag(SmartRuleCodes.CorrelationPartial, unavailablePairs.ToString(CultureInfo.InvariantCulture) + " pair(s) had no correlation figure and were not checked"));
        }

        AddHalfFlag(flags, "correlation", afterGate.Count, afterGate.Count - primary.Count, options);

        // 3. Equity-curve throttle.
        var throttle = EquityThrottle.Evaluate(inputs.TakenTrades, decisionDate, options);
        if (throttle.Status == RuleStatus.Unavailable)
        {
            flags.Add(new PlanFlag(SmartRuleCodes.ThrottleUnavailable, "equity throttle " + throttle.Reason));
        }

        return new SmartPlan(
            primary,
            [.. reserve.OrderBy(r => r.Ranked.Rank)],
            gate,
            throttle,
            status,
            unavailablePairs,
            gateRiskMultiplier * throttle.RiskMultiplier,
            flags,
            MetaSizeMultiplierApplied: false);
    }

    private static void AddHalfFlag(List<PlanFlag> flags, string rule, int seen, int removed, SmartRulesOptions options)
    {
        if (seen > 0 && (double)removed / seen >= options.HonestyFraction)
        {
            flags.Add(new PlanFlag(
                SmartRuleCodes.RuleRemovedHalf,
                string.Create(CultureInfo.InvariantCulture, $"rule {rule} removed {removed} of {seen} candidates (half or more): review it before trusting it")));
        }
    }

    private static (List<RankedCandidate> Primary, RuleStatus Status, int UnavailablePairs) ApplyCorrelation(List<RankedCandidate> input, SmartInputs inputs, SmartRulesOptions options, List<ReservedCandidate> reserve)
    {
        if (!options.CorrelationEnabled)
        {
            return ([.. input], RuleStatus.Off, 0);
        }

        if (inputs.Correlations is not { } table)
        {
            return ([.. input], RuleStatus.Unavailable, 0);
        }

        var open = (inputs.OpenBook ?? []).OrderBy(o => o.Symbol, StringComparer.Ordinal).ThenBy(o => o.Side, StringComparer.Ordinal).ToList();
        var kept = new List<RankedCandidate>();
        var unavailable = 0;
        foreach (var r in input)
        {
            var c = r.Merged.Winner.Candidate;
            var hit = Find(table, c, open.Select(o => (o.Symbol, o.Side)), SmartRuleCodes.SameBetOpenTrade, "open trade", options, ref unavailable)
                ?? Find(table, c, kept.Select(k => (k.Merged.Winner.Candidate.Symbol, k.Merged.Winner.Candidate.Side)), SmartRuleCodes.SameBetBetterTicket, "better ticket", options, ref unavailable);
            if (hit is null)
            {
                kept.Add(r);
            }
            else
            {
                reserve.Add(new ReservedCandidate(r, hit.Value.Code, hit.Value.Reason));
            }
        }

        return (kept, RuleStatus.Applied, unavailable);
    }

    private static (string Code, string Reason)? Find(CorrelationTable table, PlanCandidate c, IEnumerable<(string Symbol, string Side)> others, string code, string label, SmartRulesOptions options, ref int unavailable)
    {
        double best = double.NegativeInfinity;
        string? reason = null;
        var mine = SideSign.Of(c.Side);
        var mySymbol = CorrelationMatrix.Normalize(c.Symbol);
        foreach (var (symbol, side) in others)
        {
            // A side that is not exactly BUY or SELL is unknown: the pair is not checked and is counted, never a pass.
            var theirs = SideSign.Of(side);
            if (mine == 0 || theirs == 0)
            {
                unavailable++;
                continue;
            }

            var sameSide = mine == theirs;
            double effective;
            string text;
            if (string.Equals(mySymbol, CorrelationMatrix.Normalize(symbol), StringComparison.Ordinal))
            {
                if (!sameSide)
                {
                    continue;
                }

                effective = 1;
                text = $"same bet as {label} {symbol} (same instrument and side)";
            }
            else if (table.SameUnderlying(c.Symbol, symbol))
            {
                if (!sameSide)
                {
                    continue; // opposite sides of one underlying are a hedge
                }

                effective = 1;
                text = $"same bet as {label} {symbol} (same underlying)";
            }
            else if (table.Get(c.Symbol, symbol) is { } corr)
            {
                effective = mine * theirs * corr;
                if (Math.Round(effective, 9) <= Math.Round(options.CorrelationMax, 9))
                {
                    continue;
                }

                text = string.Create(CultureInfo.InvariantCulture, $"same bet as {label} {symbol}: correlation {effective:0.###} > {options.CorrelationMax:0.###}");
            }
            else
            {
                unavailable++;
                continue;
            }

            if (effective > best)
            {
                best = effective;
                reason = text;
            }
        }

        return reason is null ? null : (code, reason);
    }
}
