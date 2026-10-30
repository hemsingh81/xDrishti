using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Domain.Recon;

public enum ReconKind
{
    Continuous,
    Eod,
    Final,
    Recovery,
}

/// <summary>The eight break kinds of spec P12-06. All but <see cref="ForeignConflict"/> ask for a freeze.</summary>
public enum BreakKind
{
    PositionMismatch,
    TradeMissing,
    TradeExtra,
    OrderMismatch,
    FillMismatch,
    UnknownXdOrder,
    ForeignConflict,
    Unavailable,
}

public static class BreakKinds
{
    public static string Code(BreakKind kind) => kind switch
    {
        BreakKind.PositionMismatch => "position_mismatch",
        BreakKind.TradeMissing => "trade_missing",
        BreakKind.TradeExtra => "trade_extra",
        BreakKind.OrderMismatch => "order_mismatch",
        BreakKind.FillMismatch => "fill_mismatch",
        BreakKind.UnknownXdOrder => "unknown_xd_order",
        BreakKind.ForeignConflict => "foreign_conflict",
        _ => "unavailable",
    };

    public static BreakKind? Parse(string code) => Enum.GetValues<BreakKind>().Cast<BreakKind?>().FirstOrDefault(k => Code(k!.Value) == code);

    public static string Severity(BreakKind kind) => kind == BreakKind.Unavailable ? "warn" : "critical";

    /// <summary>True for every kind but the foreign conflict: those ask for a freeze, the foreign conflict asks for entries.</summary>
    public static bool AsksFreeze(BreakKind kind) => kind != BreakKind.ForeignConflict;
}

/// <summary>One leg as our record has it. <see cref="LastChangeAt"/> is the time of its latest change (the grace window counts from it).</summary>
public sealed record OurLegView(
    long IntentId,
    string ClientOrderId,
    string? BrokerOrderId,
    LegKind Kind,
    LegStatus Status,
    int Qty,
    int FilledQty,
    string SecurityId,
    ExecSide Side,
    DateTimeOffset LastChangeAt,
    int? LegNo = null,
    string? ParentBrokerOrderId = null);

/// <summary>One fill row we hold, with the leg's client id.</summary>
public sealed record OurFillView(string ExchangeTradeId, string ClientOrderId, int Qty, decimal Price, string SecurityId, ExecSide Side);

public sealed record OurPositionView(string SecurityId, int NetQty);

/// <summary>An intent as the recovery and the end of day need it.</summary>
public sealed record OurIntentView(long IntentId, Guid AccountId, ExecState State, string SecurityId, ExecSide Side, int Qty, int FilledQty, decimal Stop, decimal Entry);

/// <summary>Everything the pure comparison needs for one account.</summary>
public sealed record ReconInput(
    Guid AccountId,
    DateTimeOffset Now,
    TimeSpan Grace,
    IReadOnlyList<OurLegView> Legs,
    IReadOnlyList<OurFillView> Fills,
    IReadOnlyList<OurPositionView> Positions,
    IReadOnlyList<BrokerOrderView> BrokerOrders,
    IReadOnlyList<BrokerTradeView> BrokerTrades,
    IReadOnlyList<BrokerPositionView> BrokerPositions,
    IReadOnlySet<string> Accepted,
    IReadOnlySet<string> PreviousMissingTrades);

/// <summary>A break found by one run. <see cref="Key"/> identifies it across runs (kind, subject and the broker's figure), so a resolved break is not raised again while the broker's figure stays the same and is raised again when it changes.</summary>
public sealed record ReconBreakDraft(BreakKind Kind, string Key, string? Symbol, long? IntentId, string Ours, string Theirs, string Detail);

/// <summary>A broker item we did not place that explains part of a position, or conflicts with it.</summary>
public sealed record ForeignItem(string SecurityId, int NetQty, int Orders);

public sealed record ReconResult(
    IReadOnlyList<ReconBreakDraft> Breaks,
    int Orders,
    int Trades,
    int Positions,
    int AcceptedSkipped,
    IReadOnlyList<ForeignItem> Foreign,
    IReadOnlySet<string> MissingTrades);
