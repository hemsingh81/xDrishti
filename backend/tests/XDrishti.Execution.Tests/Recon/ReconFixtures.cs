using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Recon;

namespace XDrishti.Execution.Tests.Recon;

/// <summary>Builders for the reconciliation tests: one account, one managed instrument (security 1594) and a clean day that the tests then disturb with exactly one drift.</summary>
internal static class ReconFixtures
{
    public const string Security = "1594";
    public const string Cid = "XD261030000169E1";
    public static readonly Guid Account = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    public static string Msg(string orderNo, string? correlation, string status, int qty, int traded, string time, int legNo = 1, string avg = "0") =>
        $"{{\"Data\":{{\"OrderNo\":\"{orderNo}\",\"CorrelationId\":\"{correlation}\",\"Status\":\"{status}\",\"TxnType\":\"B\",\"Product\":\"I\",\"OrderType\":\"LMT\",\"LegNo\":{legNo},\"Quantity\":{qty},\"TradedQty\":{traded},\"Price\":100,\"TriggerPrice\":0,\"AvgTradedPrice\":{avg},\"LastUpdatedTime\":\"{time}\"}},\"Type\":\"order_alert\"}}";

    public static OurLegView Leg(string cid = Cid, LegStatus status = LegStatus.Traded, int qty = 10, int filled = 10, string? brokerId = "B1", DateTimeOffset? changed = null, LegKind kind = LegKind.Entry, long intent = 361, string security = Security, ExecSide side = ExecSide.Buy, int? legNo = null, string? parent = null) =>
        new(intent, cid, brokerId, kind, status, qty, filled, security, side, changed ?? Now.AddMinutes(-5), legNo, parent);

    public static BrokerOrderView Order(string id = "B1", string? cid = Cid, string status = "TRADED", int qty = 10, int filled = 10, string security = Security, ExecSide side = ExecSide.Buy, string? legName = null, string? parent = null) =>
        new(id, cid, status, security, side, qty, filled, 100m, null, legName, parent);

    public static BrokerTradeView Trade(string order = "B1", string id = "T1", int qty = 10, decimal price = 100m, string security = Security, ExecSide side = ExecSide.Buy) => new(order, security, side, qty, price, id);

    public static OurFillView Fill(string id = "T1", string cid = Cid, int qty = 10, decimal price = 100m, string security = Security, ExecSide side = ExecSide.Buy) => new(id, cid, qty, price, security, side);

    /// <summary>The clean day: one filled entry of 10, its trade, its fill, and a position of 10 on both sides.</summary>
    public static ReconInput Clean() => new(
        Account, Now, Grace, [Leg()], [Fill()], [new OurPositionView(Security, 10)],
        [Order()], [Trade()], [new BrokerPositionView(Security, 10)], new HashSet<string>(), new HashSet<string>());

    public static ReconKindSet Kinds(ReconResult r) => new([.. r.Breaks.Select(b => b.Kind)]);

    internal sealed record ReconKindSet(IReadOnlyList<BreakKind> Kinds);
}
