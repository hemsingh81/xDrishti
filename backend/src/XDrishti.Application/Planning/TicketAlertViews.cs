using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Planning;

/// <summary>The crossings, prompts and acts of one plan for the ticket cards (spec P7-20, ADR 0115). Read only; the same text rules as the bell, with the detail the plan screen may show.</summary>
public sealed record GetPlanAlertsQuery(long PlanId) : IQuery<Result<PlanAlertsView>>;

/// <summary>
/// The alerts of a plan. <c>enabled</c> is the setting <c>Planning:TicketAlerts:Enabled</c>; when false the lists are empty. <c>mutedItems</c> are the tickets whose further crossings the owner stopped. Armed means watched; every text ends with
/// the fact that no order is placed.
/// </summary>
public sealed record PlanAlertsView(bool Enabled, IReadOnlyList<CrossingView> Crossings, IReadOnlyList<PromptView> Prompts, IReadOnlyList<long> MutedItems);

/// <summary>One stored crossing. <c>noticeId</c> is <c>crossing:{id}</c>; <c>state</c> is shown, snoozed or dismissed; <c>text</c> is built by the server and is plain text.</summary>
public sealed record CrossingView(long Id, string NoticeId, long PlanId, long ItemId, string Kind, decimal Level, string? StopAnchor, decimal Price, DateTimeOffset TickAt, string Text, string State, DateTimeOffset? SnoozedUntil, int SnoozesLeft);

/// <summary>One post-trade prompt with its derived status (open, matched, expired_no_fill, ambiguous) and, when matched, how (journal or trade_on_record). <c>sameTradeItems</c> names the other tickets of an ambiguous prompt.</summary>
public sealed record PromptView(long Id, string NoticeId, long PlanId, long ItemId, Guid AccountId, DateOnly Session, string Status, string? Via, IReadOnlyList<long> SameTradeItems, string Text, string State, DateTimeOffset? SnoozedUntil, int SnoozesLeft);

/// <summary>Mute the further crossings of a ticket for its session (a notification preference; the ticket does not change).</summary>
public sealed record MuteTicketAlertsCommand(long ItemId, string Actor) : ICommand;

/// <summary>Hide a notice (<c>crossing:{id}</c> or <c>prompt:{id}</c>) for 15, 30 or 60 minutes; at most three times.</summary>
public sealed record SnoozeTicketNoticeCommand(string NoticeId, int Minutes, string Actor) : ICommand;

/// <summary>Hide a notice for good.</summary>
public sealed record DismissTicketNoticeCommand(string NoticeId, string Actor) : ICommand;
