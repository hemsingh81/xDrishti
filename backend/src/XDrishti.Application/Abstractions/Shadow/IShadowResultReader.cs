namespace XDrishti.Application.Abstractions.Shadow;

/// <summary>One shadow candidate with its grade as the summary reads it.</summary>
/// <param name="SessionDate">The session.</param>
/// <param name="TradeKey">The trade key.</param>
/// <param name="TicketGroup">primary, reserve or rejected.</param>
/// <param name="CountsForEdge">Whether the row is edge evidence.</param>
/// <param name="Status"><c>graded</c>, <c>not_triggered</c>, <c>not_gradable</c>, or <c>pending</c> while no grade is stored.</param>
/// <param name="RSim">The grade's R (net when charged, else gross), or null while not graded.</param>
/// <param name="RBasis"><c>net</c> or <c>gross</c>, or null while not graded.</param>
/// <param name="SheetId">The charge sheet that charged a net grade, or null.</param>
/// <param name="RGross">The same trade's R before charges (the replay's filled R), or null while not graded.</param>
/// <param name="EdgeEpoch">The paper epoch in force when the candidate was written, or null when there was none (spec CF-09 reads the current epoch's rows only).</param>
/// <param name="InSet">Whether the candidate's strategy version is in the frozen evidence set of its epoch (spec AM-01 GAP-09); false (labelled <c>outside_set</c>) also when the epoch has no set. Only a row inside the set counts for edge and for the readiness forward rows. The reader always sets it; the default is for callers that build a row by hand.</param>
public sealed record ShadowResultRow(DateOnly SessionDate, string TradeKey, string TicketGroup, bool CountsForEdge, string Status, double? RSim, string? RBasis, string? SheetId, double? RGross, long? EdgeEpoch = null, bool InSet = true);

/// <summary>The size and head of the chain, for the summary.</summary>
/// <param name="Sessions">Stored sessions, ok and gap.</param>
/// <param name="Gaps">Of those, gap sessions (the writer did not run before the open).</param>
/// <param name="LastSession">The newest stored session, or null.</param>
/// <param name="LastDigest">Its digest, or null.</param>
public sealed record ShadowChainTotals(int Sessions, int Gaps, DateOnly? LastSession, string? LastDigest);

/// <summary>
/// The only way to read shadow outcomes (spec CF-08, D6). It is referenced by the Learning, Reports and Api use cases and by no part of Planning, Selection or Autonomy (an architecture test): the rule that chooses what to approve, the automatic rule and
/// reserve promotion must not read shadow outcomes of the epoch they decide for. Read only.
/// </summary>
public interface IShadowResultReader
{
    /// <summary>Every shadow candidate with its grade, oldest first; a candidate without a stored grade reads <c>pending</c>.</summary>
    ValueTask<IReadOnlyList<ShadowResultRow>> ResultsAsync(CancellationToken cancellationToken);

    /// <summary>How many sessions the chain holds, how many are gaps, and its newest session with its digest.</summary>
    ValueTask<ShadowChainTotals> ChainAsync(CancellationToken cancellationToken);
}
