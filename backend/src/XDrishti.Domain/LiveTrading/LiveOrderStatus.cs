namespace XDrishti.Domain.LiveTrading;

/// <summary>The seven order states of the order book (spec P11-01), as stored. The database repeats the set.</summary>
public static class LiveOrderStatus
{
    public const string Transit = "transit";
    public const string Pending = "pending";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string PartTraded = "part_traded";
    public const string Traded = "traded";
    public const string Expired = "expired";

    public static IReadOnlyList<string> All { get; } = [Transit, Pending, Rejected, Cancelled, PartTraded, Traded, Expired];

    /// <summary>Reads the broker's word (any case); null when it is none of the seven.</summary>
    public static string? Parse(string? text) => text?.Trim().ToLowerInvariant() is { } s && All.Contains(s, StringComparer.Ordinal) ? s : null;

    /// <summary>The states that stand between the owner and a stop that is, or can still be, in force.</summary>
    public static bool CountsAsStop(string status) => status is Transit or Pending or Traded or PartTraded;
}
