using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Paper;

/// <summary>Rupees of one paper account (rupee lines are per account; R is counted once per ticket).</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Amount">The rupees, after the stored sheet's charges when charged.</param>
public sealed record AccountRupees(Guid AccountId, decimal Amount);

/// <summary>One line of the discipline report (spec P10-03): a count, its R as a sum (a fact) and a mean (an estimate with a minimum n), rupees per account and a note.</summary>
/// <param name="Id">The category or line name.</param>
/// <param name="Kind">category, flag, deviation or total.</param>
/// <param name="Title">The words.</param>
/// <param name="N">Tickets (or fills) in the line.</param>
/// <param name="SumR">The sum of R of the line's graded tickets.</param>
/// <param name="MeanR">The mean R: insufficient below 15 graded, provisional below 30.</param>
/// <param name="Rupees">Rupees per account; empty when none apply.</param>
/// <param name="Note">What the line counts apart, in words.</param>
public sealed record DisciplineLine(string Id, string Kind, string Title, int N, Metric SumR, Metric MeanR, IReadOnlyList<AccountRupees> Rupees, string Note);
