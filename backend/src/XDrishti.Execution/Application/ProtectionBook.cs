using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Application;

/// <summary>Reads our protective legs out of the broker's order book. Only a SELL leg of ours counts: a stop by its client id kind, a super-order stop leg by its leg name. An unreadable id is not ours and protects nothing.</summary>
public static class ProtectionBook
{
    public const string SuperStopLeg = "STOP_LOSS_LEG";

    private static readonly string[] Resting = ["TRANSIT", "PENDING", "PART_TRADED"];

    public static bool IsStop(BrokerOrderView o) =>
        o.Side == ExecSide.Sell
        && ((o.ClientOrderId is { } c && ClientOrderId.TryParse(c, out var id) && id.Kind == LegKind.Stop) || string.Equals(o.LegName, SuperStopLeg, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<StopLegView> StopLegs(IReadOnlyList<BrokerOrderView> book, string securityId) =>
        [.. book.Where(o => o.SecurityId == securityId && IsStop(o)).Select(o => new StopLegView(o.ClientOrderId ?? o.OrderId, Math.Max(0, o.Qty - o.FilledQty), o.Status, o.Trigger ?? o.Price))];

    /// <summary>Our resting exit and flatten sells for the security, with the quantity still to fill.</summary>
    public static IReadOnlyList<BrokerOrderView> ExitsResting(IReadOnlyList<BrokerOrderView> book, string securityId) =>
        [.. book.Where(o => o.SecurityId == securityId && o.Side == ExecSide.Sell && Resting.Contains(o.Status) && o.ClientOrderId is { } c && ClientOrderId.TryParse(c, out var id) && id.Kind is LegKind.Exit or LegKind.Flatten)];

    public static int ExitsInFlight(IReadOnlyList<BrokerOrderView> book, string securityId) => ExitsResting(book, securityId).Sum(o => Math.Max(0, o.Qty - o.FilledQty));
}
