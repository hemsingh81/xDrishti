namespace XDrishti.Domain.Selection;

/// <summary>One cluster of symbols that count as one bet. <see cref="Id"/> is the smallest member (ordinal, upper case), so it does not depend on the order of the input.</summary>
/// <param name="Id">The smallest member symbol.</param>
/// <param name="Members">The members, ascending (ordinal, upper case).</param>
/// <param name="CorrelationUnknown">True for a singleton whose correlation with every other symbol is unknown: it is alone because nothing is known, not because it is unrelated.</param>
public sealed record CorrelationCluster(string Id, IReadOnlyList<string> Members, bool CorrelationUnknown)
{
    /// <summary>The members as the ticket text shows them, <c>{A, B}</c>.</summary>
    public string MembersText => "{" + string.Join(", ", Members) + "}";
}
