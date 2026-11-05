namespace XDrishti.Application.Abstractions.Shadow;

/// <summary>A shadow candidate that has no grade yet.</summary>
/// <param name="EntryId">The shadow entry.</param>
/// <param name="SessionDate">Its session.</param>
/// <param name="PlanId">The plan it came from.</param>
/// <param name="PlanItemId">The plan item it came from.</param>
public sealed record UngradedShadowEntry(long EntryId, DateOnly SessionDate, long PlanId, long PlanItemId);

/// <summary>A grade to store: the replay's answer for one shadow entry (the same fields as <c>paper.shadow_grades</c>).</summary>
/// <param name="EntryId">The shadow entry.</param>
/// <param name="Status"><c>graded</c>, <c>not_triggered</c> or <c>not_gradable</c>.</param>
/// <param name="RSim">The grade's R (net when charged, else gross); only when graded.</param>
/// <param name="RBasis"><c>net</c> or <c>gross</c>; only when graded.</param>
/// <param name="RGross">The same trade's R before charges (the replay's filled R, which TicketGrader uses when the trade is not charged); only when graded. Equals <paramref name="RSim"/> when the basis is gross.</param>
/// <param name="EntryAt">The entry minute of the simulated trade, or null.</param>
/// <param name="ExitAt">The exit minute of the simulated trade, or null.</param>
/// <param name="ExitReason">Why the simulated trade ended, or null.</param>
/// <param name="EngineVersion">The engine that replayed it.</param>
/// <param name="SheetId">The charge sheet that charged a net grade, or null.</param>
public sealed record NewShadowGrade(long EntryId, string Status, double? RSim, string? RBasis, double? RGross, DateTimeOffset? EntryAt, DateTimeOffset? ExitAt, string? ExitReason, string EngineVersion, string? SheetId);

/// <summary>
/// The grades of the shadow record (<c>paper.shadow_grades</c>; spec CF-08, ADR 0212) behind a port: read the entries that have none, insert a grade once. No member updates or deletes; the unique key on the entry refuses a second grade, so a graded row is never rewritten.
/// </summary>
public interface IShadowGradeStore
{
    /// <summary>The entries that count for edge, are for a session up to <paramref name="through"/> and have no grade, newest session first, at most <paramref name="take"/>.</summary>
    ValueTask<IReadOnlyList<UngradedShadowEntry>> UngradedAsync(DateOnly through, int take, CancellationToken cancellationToken);

    /// <summary>Inserts the grades; an entry that has one already is left alone. Returns how many were stored.</summary>
    ValueTask<int> InsertAsync(IReadOnlyList<NewShadowGrade> grades, CancellationToken cancellationToken);
}
