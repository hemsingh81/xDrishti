using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>The outcome of one set: the state and, for a failure, the thresholds that did not hold, in words.</summary>
public sealed record SetOutcome(string Set, SetState State, ValueList<string> Reasons)
{
    public static SetOutcome Of(string set, SetState state, params string[] reasons) => new(set, state, new ValueList<string>(reasons));
}
