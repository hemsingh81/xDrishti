using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Planning;

/// <summary>The notices about the nightly plan for the top-bar bell, derived on read from the stored plan record, plan runs and fills (P8-15, ADR 0101). Read only: nothing is stored and nothing is written.</summary>
public sealed record GetPlanNoticesQuery : IQuery<PlanNotices>;

/// <summary>
/// One thing about the plan the owner should know. <c>kind</c>: plan_awaiting_review · plan_nightly_failed · plan_tickets_lapsed · plan_tickets_armed · plan_tickets_invalidated · plan_fills_unmatched.
/// <c>severity</c>: error · warning · info. <c>id</c> carries what changes, so a worse or bigger situation is unread again: <c>plan:review:{planId}:{info|warning}</c>, <c>plan:{lapsed|armed|invalidated|fills|fills-ambiguous}:{planId}:{count}</c>, <c>plan:nightly:{yyyy-MM-dd}:{attempt}</c>. <c>route</c> is an in-app path (<c>/plans?plan={id}</c>, or <c>/plans</c>
/// for a run that failed closed and so has no plan); there is no command: the notice only shows and opens. <c>at</c> is the moment it is about (the cutoff, the end of the failed run), or null. The text has counts, never a symbol, a size or a price.
/// </summary>
public sealed record PlanNotice(string Id, string Kind, string Severity, string Text, string Route, long? PlanId, DateTimeOffset? At);

/// <summary>The current plan notices, worst first. An item is present while its cause stands and absent once the stored state has changed.</summary>
public sealed record PlanNotices(IReadOnlyList<PlanNotice> Items);
