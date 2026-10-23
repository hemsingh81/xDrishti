namespace XDrishti.Domain.Selection;

/// <summary>The stable codes of the smart rules (spec P7-06). Stored and shown as they are; never rename one.</summary>
public static class SmartRuleCodes
{
    /// <summary>Gate: normal.</summary>
    public const string GateNormal = "gate_normal";

    /// <summary>Gate: reduced.</summary>
    public const string GateReduced = "gate_reduced";

    /// <summary>Gate: no trade.</summary>
    public const string GateNoTrade = "gate_no_trade";

    /// <summary>Gate: not known; the plan says so and does not treat it as normal.</summary>
    public const string GateUnavailable = "gate_unavailable";

    /// <summary>Gate switched off by the owner.</summary>
    public const string GateOff = "gate_off";

    /// <summary>Reserve reason: the gate cancelled the plan.</summary>
    public const string ReserveGateNoTrade = "gate_no_trade";

    /// <summary>Reserve reason: the gate halved the items.</summary>
    public const string ReserveGateReduced = "gate_reduced_cut";

    /// <summary>Throttle: equity at or above its average.</summary>
    public const string ThrottleClear = "throttle_clear";

    /// <summary>Throttle: equity below its average, risk cut.</summary>
    public const string ThrottleCut = "throttle_cut";

    /// <summary>Throttle: not enough real taken trades.</summary>
    public const string ThrottleUnavailable = "throttle_unavailable";

    /// <summary>Throttle switched off.</summary>
    public const string ThrottleOff = "throttle_off";

    /// <summary>Correlation rule: no usable table.</summary>
    public const string CorrelationUnavailable = "correlation_unavailable";

    /// <summary>Some pairs had no figure; those pairs were not checked.</summary>
    public const string CorrelationPartial = "correlation_partial";

    /// <summary>Reserve reason: the same bet as a better ticket.</summary>
    public const string SameBetBetterTicket = "same_bet_better_ticket";

    /// <summary>Reserve reason: the same bet as an open trade.</summary>
    public const string SameBetOpenTrade = "same_bet_open_trade";

    /// <summary>Flag: a rule removed half or more of the candidates it saw (doc 09 section 14).</summary>
    public const string RuleRemovedHalf = "rule_removed_half";
}
