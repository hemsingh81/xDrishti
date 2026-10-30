namespace XDrishti.Execution.Domain.Recon;

/// <summary>The status codes of the order-update stream (UNVERIFIED against Dhan beyond one documentation page). A part fill is <see cref="Pending"/> with a traded quantity below the order quantity.</summary>
public enum UpdateStatus
{
    Transit,
    Pending,
    Rejected,
    Cancelled,
    Traded,
    Expired,
}

/// <summary>
/// One order update, reduced to the mapped fields (spec P12-06). Nothing else of the message is kept: no remarks, no client id, no token. <see cref="LegNo"/> is 1 entry, 2 stop, 3 target. <see cref="CorrelationId"/> is our client order id when the order is ours.
/// </summary>
public sealed record OrderUpdate(
    string OrderNo,
    string? CorrelationId,
    UpdateStatus Status,
    int LegNo,
    ExecSide Side,
    string Product,
    string OrderType,
    int Quantity,
    int TradedQty,
    decimal Price,
    decimal TriggerPrice,
    decimal AvgTradedPrice,
    string? ExchOrderNo,
    string? AlgoId,
    string? Source,
    string? LastUpdatedTime)
{
    /// <summary>The dedup key: an update is applied once per order, status, traded quantity and time.</summary>
    public string Key => $"{OrderNo}|{Status}|{TradedQty}|{LastUpdatedTime}";

    /// <summary>The leg status this update means for our <see cref="Leg"/>: a pending order with a part fill is <see cref="LegStatus.PartTraded"/>.</summary>
    public LegStatus LegStatus => Status switch
    {
        UpdateStatus.Transit => LegStatus.Transit,
        UpdateStatus.Pending => TradedQty > 0 && TradedQty < Quantity ? LegStatus.PartTraded : LegStatus.Pending,
        UpdateStatus.Traded => LegStatus.Traded,
        UpdateStatus.Rejected => LegStatus.Rejected,
        UpdateStatus.Cancelled => LegStatus.Cancelled,
        _ => LegStatus.Expired,
    };
}

public enum UpdateParseKind
{
    /// <summary>An order update was read.</summary>
    Update,

    /// <summary>A well-formed message of another kind (a login answer): nothing to apply, nothing wrong.</summary>
    NotAnOrderAlert,

    /// <summary>Garbage, a missing field or an unknown code: dropped, counted, and a poll is asked for. Never a guess.</summary>
    Unreadable,
}

public sealed record UpdateParseResult(UpdateParseKind Kind, OrderUpdate? Update, string? Reason)
{
    public static UpdateParseResult Of(OrderUpdate update) => new(UpdateParseKind.Update, update, null);

    public static UpdateParseResult Unreadable(string reason) => new(UpdateParseKind.Unreadable, null, reason);
}
