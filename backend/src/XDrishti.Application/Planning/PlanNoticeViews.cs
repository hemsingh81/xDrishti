using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Planning;

/// <summary>The notices about the nightly plan for the top-bar bell, derived on read from the stored plan record, plan runs and fills (P8-15, ADR 0101). Read only: nothing is stored and nothing is written.</summary>
public sealed record GetPlanNoticesQuery : IQuery<PlanNotices>;

/// <summary>
/// One thing about the plan the owner should know. <c>kind</c>: plan_awaiting_review · plan_nightly_failed · plan_tickets_lapsed · plan_tickets_armed · plan_tickets_invalidated · plan_fills_unmatched · ticket_level_crossed · post_trade_prompt (these two are carried from P7-20 as it derives them, only while ticket alerts are enabled; their ids are <c>crossing:{id}</c> and <c>prompt:{id}</c>) · compliance_recheck_overdue (carried from P11-05: the newest compliance record of a kind has a review date that has passed; id <c>compliance:overdue:{kind}:{yyyy-MM-dd}</c>, route <c>/live?tab=compliance</c>, resolved by a newer record of the kind).
/// <c>severity</c>: error · warning · info. <c>id</c> carries what changes, so a worse or bigger situation is unread again: <c>plan:review:{planId}:{info|warning}</c>, <c>plan:{lapsed|armed|invalidated|fills|fills-ambiguous}:{planId}:{count}</c>, <c>plan:nightly:{yyyy-MM-dd}:{attempt}</c>. <c>route</c> is an in-app path (<c>/plans?plan={id}</c>, or <c>/plans</c>
/// for a run that failed closed and so has no plan); there is no command: the notice only shows and opens. <c>at</c> is the moment it is about (the cutoff, the end of the failed run), or null. <c>since</c> is when the situation began and <c>resolved</c> marks a carried ticket notice that has closed but is still shown for a day (plan items just disappear). The text has counts, never a symbol, a size or a price.
/// </summary>
public sealed record PlanNotice(string Id, string Kind, string Severity, string Text, string Route, long? PlanId, DateTimeOffset? At, DateTimeOffset? Since = null, bool Resolved = false);

/// <summary>The current plan notices, worst first. An item is present while its cause stands and absent once the stored state has changed.</summary>
public sealed record PlanNotices(IReadOnlyList<PlanNotice> Items);
