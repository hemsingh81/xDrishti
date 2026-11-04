namespace XDrishti.Domain.Research;

/// <summary>One closed trade of one candidate as the exit choice reads it (P4-06, D1): which candidate, the India dates of the entry and the exit, and R in the unit of the run.</summary>
/// <param name="CandidateId">The candidate id.</param>
/// <param name="Entry">India date of the entry fill.</param>
/// <param name="Exit">India date of the exit.</param>
/// <param name="R">R in the unit of the run (<c>RNet</c> costed, <c>RFilled</c> gross).</param>
/// <param name="RExact">R from the exact prices of a costed trade (CF-05: the cost stress restates the trade from it and <paramref name="R"/>); null for a gross trade. Read by nothing in the choice of an exit.</param>
public sealed record SelectionTrade(string CandidateId, DateOnly Entry, DateOnly Exit, double R, double? RExact = null);
