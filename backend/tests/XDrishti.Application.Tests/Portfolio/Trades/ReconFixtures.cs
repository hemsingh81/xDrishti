using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Trades;

/// <summary>Builders for the reconstruction tests: trades in the order they were stored, applied actions, and the sum of what the open lots cost.</summary>
internal static class ReconFixtures
{
    public static readonly DateOnly FirstImport = new(2025, 1, 1);

    private static readonly DateTimeOffset Stored = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    public static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    public static ReconTrade Buy(string date, long qty, decimal price, string? time = null, int seq = 0) => Trade(TradeSide.Buy, date, qty, price, time, seq);

    public static ReconTrade Sell(string date, long qty, decimal price, string? time = null, int seq = 0) => Trade(TradeSide.Sell, date, qty, price, time, seq);

    private static ReconTrade Trade(TradeSide side, string date, long qty, decimal price, string? time, int seq) =>
        new(D(date), time is null ? null : TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture), side, qty, price, Stored, seq, Guid.NewGuid());

    public static AppliedAction Split(string exDate, int oldShares, int newShares) =>
        new(D(exDate), CorporateActionKind.Split, oldShares, newShares, CorporateActionRatio.Factor(CorporateActionKind.Split, oldShares, newShares));

    public static AppliedAction Bonus(string exDate, int bonus, int held) =>
        new(D(exDate), CorporateActionKind.Bonus, bonus, held, CorporateActionRatio.Factor(CorporateActionKind.Bonus, bonus, held));

    public static Reconstruction Run(long qty, decimal avgCost, IEnumerable<ReconTrade> trades, IReadOnlyList<AppliedAction>? actions = null, DateOnly? firstImport = null) =>
        LotReconstructor.Reconstruct(new ReconInput(qty, avgCost, firstImport ?? FirstImport, [.. trades], actions ?? []));

    public static decimal OpenCost(this Reconstruction result) => result.Lots.Where(l => l.IsOpen).Sum(l => l.Qty * l.UnitCost);

    public static long OpenQty(this Reconstruction result) => result.Lots.Where(l => l.IsOpen).Sum(l => l.Qty);
}
