namespace XDrishti.Domain.Learning;

/// <summary>The answer of the influence predicate for one model (CF-10 part 10c-2): the four terms and, derived from them and from nothing else, whether the model may influence a decision.</summary>
/// <param name="Terms">The terms in the order of <see cref="MlInfluence.TermNames"/>.</param>
public sealed record MlInfluenceResult(IReadOnlyList<MlTermResult> Terms)
{
    /// <summary>True only when the terms are exactly the four of <see cref="MlInfluence.TermNames"/>, in that order, and every one holds. Any other list is false.</summary>
    public bool MayInfluence => Terms.Select(t => t.Name).SequenceEqual(MlInfluence.TermNames, StringComparer.Ordinal) && Terms.All(t => t.Holds);

    /// <summary>Every reason of every term that does not hold, in term order; empty only when <see cref="MayInfluence"/>.</summary>
    public IReadOnlyList<MlInfluenceReason> Reasons => [.. Terms.SelectMany(t => t.Reasons)];
}
