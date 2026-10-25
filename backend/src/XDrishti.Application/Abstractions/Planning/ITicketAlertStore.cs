using XDrishti.Domain.Planning;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>An armed ticket with numeric levels that the live feed watches (not muted). <paramref name="Side"/> is <c>BUY</c> or <c>SELL</c>.</summary>
public sealed record WatchedTicket(long ItemId, long PlanId, string Symbol, string Side, DateOnly Session, decimal? Stop, string? StopAnchor, decimal? Target);

/// <summary>A crossing to store.</summary>
public sealed record NewCrossing(long PlanId, long ItemId, DateOnly Session, string Kind, decimal Level, string? StopAnchor, decimal Price, DateTimeOffset TickAt);

/// <summary>A stored crossing.</summary>
public sealed record CrossingRow(long Id, long PlanId, long ItemId, DateOnly Session, string Kind, decimal Level, string? StopAnchor, decimal Price, DateTimeOffset TickAt);

/// <summary>A prompt to store: a ticket that was Armed and one of its paper allocation accounts.</summary>
public sealed record NewPrompt(long PlanId, long ItemId, Guid AccountId, DateOnly Session);

/// <summary>A stored prompt with the ticket's symbol and side.</summary>
public sealed record PromptRow(long Id, long PlanId, long ItemId, Guid AccountId, DateOnly Session, string Symbol, string Side);

/// <summary>A stored owner act on a notice or a ticket.</summary>
public sealed record AckRow(long Id, string NoticeKind, long? NoticeId, long? ItemId, string Action, DateTimeOffset? Until);

/// <summary>An act to store. <paramref name="Actor"/> is <c>owner:</c> plus the session's e-mail address.</summary>
public sealed record NewAck(string NoticeKind, long? NoticeId, long? ItemId, string Action, DateTimeOffset? Until, string Actor);

/// <summary>Where an item lives: its plan and the plan's session.</summary>
public sealed record ItemSession(long PlanId, DateOnly Session);

/// <summary>
/// The insert-only store of price-crossing alerts, post-trade prompts and the owner's mute, snooze and dismiss acts (spec P7-20, ADR 0115). Reads portfolio trades and the journal links; writes only its own three tables. Nothing here changes a ticket
/// and nothing talks to a broker.
/// </summary>
public interface ITicketAlertStore
{
    /// <summary>The armed tickets of the plans for <paramref name="session"/> that have a numeric stop or target and are not muted.</summary>
    ValueTask<IReadOnlyList<WatchedTicket>> ListWatchedAsync(DateOnly session, CancellationToken cancellationToken);

    /// <summary>Stores a crossing once per (ticket, kind); false when it already exists or the ticket is muted.</summary>
    ValueTask<bool> AddCrossingAsync(NewCrossing crossing, CancellationToken cancellationToken);

    /// <summary>The crossings of sessions from <paramref name="fromSession"/> on, optionally of one plan, oldest first.</summary>
    ValueTask<IReadOnlyList<CrossingRow>> ListCrossingsAsync(DateOnly fromSession, long? planId, CancellationToken cancellationToken);

    /// <summary>The (ticket, paper allocation account) pairs of tickets that were Armed on the plans for <paramref name="session"/> (rejected candidates never).</summary>
    ValueTask<IReadOnlyList<NewPrompt>> ListArmedOnAsync(DateOnly session, CancellationToken cancellationToken);

    /// <summary>Stores the prompts that do not exist yet; the number stored.</summary>
    ValueTask<int> AddPromptsAsync(IReadOnlyList<NewPrompt> prompts, CancellationToken cancellationToken);

    /// <summary>The prompts of sessions from <paramref name="fromSession"/> on, optionally of one plan.</summary>
    ValueTask<IReadOnlyList<PromptRow>> ListPromptsAsync(DateOnly fromSession, long? planId, CancellationToken cancellationToken);

    /// <summary>The non-voided trades of any source with a trade date in the range, and the ticket the journal linked each to.</summary>
    ValueTask<IReadOnlyList<PromptTrade>> TradesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Every act stored (the table is small: one row per click of the owner).</summary>
    ValueTask<IReadOnlyList<AckRow>> ListAcksAsync(CancellationToken cancellationToken);

    /// <summary>Stores one act.</summary>
    ValueTask AddAckAsync(NewAck ack, CancellationToken cancellationToken);

    /// <summary>Whether a crossing (<c>crossing</c>) or a prompt (<c>prompt</c>) with this id exists.</summary>
    ValueTask<bool> NoticeExistsAsync(string noticeKind, long noticeId, CancellationToken cancellationToken);

    /// <summary>The plan and session of an item; null when there is none.</summary>
    ValueTask<ItemSession?> FindItemAsync(long itemId, CancellationToken cancellationToken);
}
