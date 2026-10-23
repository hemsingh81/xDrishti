using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// Grades the tickets of past plans once (P7-13, ADR 0090): every primary, reserve and rejected ticket of the plan for <paramref name="Date"/> (or of every past plan with ungraded tickets) is walked through the stored bars of its
/// session. Insert-only and idempotent. It changes no ticket state and cannot place an order.
/// </summary>
/// <param name="Date">The planned session; null for every past plan with ungraded tickets (newest 30).</param>
public sealed record GradePlansCommand(DateOnly? Date) : ICommand;

/// <summary>What a grading run did.</summary>
/// <param name="Plans">Plans looked at.</param>
/// <param name="Graded">Tickets graded with an R.</param>
/// <param name="NotTriggered">Tickets stored as not triggered.</param>
/// <param name="NotGradable">Tickets stored as not gradable.</param>
/// <param name="Pending">Tickets left for a later run (bars missing or position still open).</param>
/// <param name="AlreadyGraded">Tickets that had a grade and were left alone.</param>
public sealed record GradeOutcome(int Plans, int Graded, int NotTriggered, int NotGradable, int Pending, int AlreadyGraded);

/// <summary>The grades of one plan with the day's report (P7-13).</summary>
/// <param name="PlanId">The plan.</param>
public sealed record GetPlanGradeQuery(long PlanId) : IQuery<Result<PlanGradeView>>;

/// <summary>The decisions report over the graded days (P7-13).</summary>
/// <param name="From">First planned session, or null.</param>
/// <param name="To">Last planned session, or null.</param>
public sealed record GetDecisionsReportQuery(DateOnly? From, DateOnly? To) : IQuery<Result<GradeReportData>>;
