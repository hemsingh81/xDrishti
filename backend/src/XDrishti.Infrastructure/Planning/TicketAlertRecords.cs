namespace XDrishti.Infrastructure.Planning;

/// <summary>A row of <c>trading.ticket_alerts</c> (spec P7-20, ADR 0115): one price crossing of a watched ticket's stored level. Insert-only; at most one per (ticket, kind).</summary>
internal sealed class TicketAlertRecord
{
    public long Id { get; set; }

    public long PlanId { get; set; }

    public long ItemId { get; set; }

    public DateOnly Session { get; set; }

    public string Kind { get; set; } = string.Empty;

    public decimal Level { get; set; }

    public string? StopAnchor { get; set; }

    public decimal Price { get; set; }

    public DateTimeOffset TickAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A row of <c>trading.ticket_prompts</c>: the post-trade question for one ticket that was Armed and one paper allocation account. Insert-only; its status is derived on read.</summary>
internal sealed class TicketPromptRecord
{
    public long Id { get; set; }

    public long PlanId { get; set; }

    public long ItemId { get; set; }

    public Guid AccountId { get; set; }

    public DateOnly Session { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A row of <c>trading.ticket_notice_acks</c>: a snooze, a dismiss or a mute by the owner. Insert-only; the newest decides; the time is stamped by the database.</summary>
internal sealed class TicketNoticeAckRecord
{
    public long Id { get; set; }

    public string NoticeKind { get; set; } = string.Empty;

    public long? NoticeId { get; set; }

    public long? ItemId { get; set; }

    public string Action { get; set; } = string.Empty;

    public DateTimeOffset? Until { get; set; }

    public string Actor { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }
}
