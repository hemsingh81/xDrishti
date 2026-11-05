namespace XDrishti.Domain.Learning;

/// <summary>One term of the influence predicate with its reading and every reason it does not hold (CF-10 part 10c-2).</summary>
/// <param name="Name">The term's name (<see cref="MlInfluence.HoldOutBeatsNoMl"/> and the others).</param>
/// <param name="State">The reading.</param>
/// <param name="Reasons">Why not; empty only when the term holds.</param>
public sealed record MlTermResult(string Name, MlTermState State, IReadOnlyList<MlInfluenceReason> Reasons)
{
    /// <summary>True only for <see cref="MlTermState.Holds"/>.</summary>
    public bool Holds => State == MlTermState.Holds;
}
