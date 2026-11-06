using XDrishti.Domain.Learning;

namespace XDrishti.Application.Confidence;

/// <summary>The state words of a kill verdict in the decay report back to the Domain enum. Anything unknown reads as not evaluated, which is never a pass.</summary>
internal static class KillStates
{
    public static KillRuleState Parse(string state) => state switch
    {
        "fires" => KillRuleState.Fires,
        "holds" => KillRuleState.Holds,
        "held" => KillRuleState.Held,
        _ => KillRuleState.NotEvaluated,
    };
}
