namespace XDrishti.Application.Abstractions.Shadow;

/// <summary>One graded shadow trade as the summary reads it.</summary>
/// <param name="SessionDate">The session.</param>
/// <param name="TradeKey">The trade key.</param>
/// <param name="TicketGroup">primary, reserve or rejected.</param>
/// <param name="CountsForEdge">Whether the row is edge evidence.</param>
/// <param name="Status">The grade status.</param>
/// <param name="RSim">The simulated result in R, or null while not graded.</param>
/// <param name="RBasis"><c>net</c> or <c>gross</c>, or null while not graded.</param>
public sealed record ShadowResultRow(DateOnly SessionDate, string TradeKey, string TicketGroup, bool CountsForEdge, string Status, double? RSim, string? RBasis);

/// <summary>
/// The only way to read shadow outcomes (spec CF-08, D6). It is referenced by the Learning, Reports and Api use cases and by no part of Planning, Selection or Autonomy (an architecture test): the rule that chooses what to approve, the automatic rule and
/// reserve promotion must not read shadow outcomes of the epoch they decide for. Read only; the implementation comes with the grader.
/// </summary>
public interface IShadowResultReader
{
    /// <summary>The graded shadow trades of the sessions in the window (all when both are null), oldest first.</summary>
    ValueTask<IReadOnlyList<ShadowResultRow>> ResultsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}
