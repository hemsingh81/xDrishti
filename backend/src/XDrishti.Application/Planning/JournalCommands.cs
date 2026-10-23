using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// Journals the fills the portfolio sync already stored (P7-14, ADR 0093): matches them to the approved tickets of a planned session, tags the rest discretionary and grades finished round trips beside the plan grade. It reads fills
/// and the plan record and writes insert-only journal rows. It changes no ticket state, writes nothing to the portfolio and cannot place an order.
/// </summary>
/// <param name="Date">The planned session; null for the newest 30 plans up to today.</param>
public sealed record JournalPlansCommand(DateOnly? Date) : ICommand;

/// <summary>A fill the journal did not match because more than one ticket fits.</summary>
/// <param name="Date">India date of the trade.</param>
/// <param name="Symbol">Symbol.</param>
/// <param name="Side">Side.</param>
/// <param name="Qty">Shares.</param>
/// <param name="Reason">Why.</param>
public sealed record UnmatchedFill(DateOnly Date, string Symbol, string Side, long Qty, string Reason);

/// <summary>What a journal run did.</summary>
/// <param name="Plans">Plans looked at.</param>
/// <param name="Tickets">Approved ticket-account pairs fills could match.</param>
/// <param name="Entries">Entry fills matched.</param>
/// <param name="Exits">Exit fills matched.</param>
/// <param name="Discretionary">Fills tagged discretionary.</param>
/// <param name="Written">New rows stored (entries and fill links).</param>
/// <param name="GradesWritten">Fill grades stored.</param>
/// <param name="GradesPending">Finished or open entries without a fill grade (position open, or plan grade not there yet).</param>
/// <param name="Unmatched">Ambiguous fills, left unmatched.</param>
public sealed record JournalOutcome(int Plans, int Tickets, int Entries, int Exits, int Discretionary, int Written, int GradesWritten, int GradesPending, IReadOnlyList<UnmatchedFill> Unmatched);

/// <summary>The journal of one plan (P7-14). Read only.</summary>
/// <param name="PlanId">The plan.</param>
public sealed record GetPlanJournalQuery(long PlanId) : IQuery<Result<PlanJournalView>>;

/// <summary>A fill as the API shows it.</summary>
/// <param name="TransactionId">The stored trade.</param>
/// <param name="Role">entry, exit or discretionary.</param>
/// <param name="Symbol">Symbol.</param>
/// <param name="Side">buy or sell.</param>
/// <param name="Date">India date.</param>
/// <param name="Time">India time, when known.</param>
/// <param name="Qty">Shares of the role.</param>
/// <param name="Price">Fill price.</param>
public sealed record JournalFillView(Guid TransactionId, string Role, string Symbol, string Side, DateOnly Date, TimeOnly? Time, long Qty, decimal Price);

/// <summary>One ticket's journal line: plan grade and fill grade side by side.</summary>
/// <param name="ItemId">The ticket.</param>
/// <param name="Rank">Rank in the plan.</param>
/// <param name="Symbol">Symbol.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="AccountId">The account.</param>
/// <param name="State">The ticket's newest state.</param>
/// <param name="Fills">Its entry and exit fills.</param>
/// <param name="Status">open (not finished), pending (finished, plan grade not there yet), graded or no_plan_levels.</param>
/// <param name="Fill">The fill grade, labelled <c>realised fills, not simulated</c>; null until graded.</param>
/// <param name="PlanStatus">The plan-price grade's status, or pending.</param>
/// <param name="PlanR">The plan-price grade's R, labelled <c>simulated on plan prices</c>.</param>
public sealed record JournalEntryView(long ItemId, int Rank, string Symbol, string Side, Guid AccountId, string State, IReadOnlyList<JournalFillView> Fills, string Status, FillGrade? Fill, string PlanStatus, double? PlanR);

/// <summary>The journal of one plan.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="ForDate">The planned session.</param>
/// <param name="FillBasis">Always <c>realised fills, not simulated</c>.</param>
/// <param name="PlanBasis">Always <c>simulated on plan prices</c>.</param>
/// <param name="Entries">Tickets with fills.</param>
/// <param name="Discretionary">The day's fills with no ticket, reported separately.</param>
/// <param name="TicketsWithoutFills">Approved tickets that no fill was matched to.</param>
public sealed record PlanJournalView(long PlanId, DateOnly ForDate, string FillBasis, string PlanBasis, IReadOnlyList<JournalEntryView> Entries, IReadOnlyList<JournalFillView> Discretionary, int TicketsWithoutFills);
