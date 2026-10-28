using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>The hand-worked session of spec P11-01 (2026-11-03, account A, every buy approved at 21:05 the evening before).</summary>
internal static class LiveFixtureDay
{
    public static readonly Guid Account = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly DateOnly Day = new(2026, 11, 3);
    public static readonly DateTimeOffset Approved = new(new DateTime(2026, 11, 2, 21, 5, 0), IndiaTime.Offset);

    public static JournalTicket Ticket(long item, string symbol) => new(item, 1, (int)item, Account, symbol, Side.Buy, Day, Approved);

    public static IReadOnlyList<JournalTicket> Tickets { get; } = [Ticket(1, "RELIANCE"), Ticket(2, "TCS"), Ticket(3, "INFY"), Ticket(4, "HDFCBANK")];

    public static LiveFill Fill(int n, string symbol, Side side, long qty, decimal price, string time, string product = "CNC", string? order = null) =>
        new(new Guid(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), Account, "t" + n, order, symbol, side, product, qty, price, Day, TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture));

    public static IReadOnlyList<LiveFill> Fills { get; } =
    [
        Fill(1, "RELIANCE", Side.Buy, 25, 1250.40m, "09:41:10"),
        Fill(2, "RELIANCE", Side.Buy, 15, 1250.60m, "09:41:30"),
        Fill(3, "RELIANCE", Side.Sell, 40, 1262.00m, "11:05:00"),
        Fill(4, "SBIN", Side.Buy, 100, 800.00m, "10:15:00"),
    ];

    public static DateTimeOffset At(string time) => new(Day.ToDateTime(TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture)), IndiaTime.Offset);

    public static LiveOrder Order(string id, string symbol, Side side, long qty, string status, long filled, string created, decimal? price = null, decimal? trigger = null, string? reject = null) =>
        new(Account, id, symbol, side, "CNC", trigger is null ? "LIMIT" : "STOP_LOSS_MARKET", "DAY", qty, price, trigger, status, filled, filled > 0 ? price : null, At(created), null, null, null, null, reject, Day);

    public static IReadOnlyList<LiveOrder> Orders { get; } =
    [
        Order("o1", "RELIANCE", Side.Buy, 40, "traded", 40, "09:40:00", 1251m),
        Order("o2", "RELIANCE", Side.Sell, 40, "pending", 0, "09:44:02", trigger: 1240m),
        Order("o3", "TCS", Side.Buy, 10, "rejected", 0, "09:50:00", 3401m, reject: "insufficient margin"),
    ];
}
