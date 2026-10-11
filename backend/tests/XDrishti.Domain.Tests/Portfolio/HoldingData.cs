using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>Synthetic holdings for the tests (made-up symbols, ids and prices).</summary>
internal static class HoldingData
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The owner's calendar day at <see cref="T0"/> (India).</summary>
    public static readonly DateOnly Today = new(2026, 10, 10);

    public static readonly DateOnly Later = new(2026, 10, 12);

    public static readonly Guid Account = Guid.Parse("00000000-0000-7000-8000-0000000000a1");

    public static readonly Guid Bucket = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    public static BrokerHolding Row(long qty = 100, decimal avgCost = 50m, decimal price = 60m, string symbol = "EXMPL", string securityId = "1001", string? isin = "INE000A01011") =>
        new("ALL", securityId, isin, symbol, qty, avgCost, price);

    /// <summary>An imported holding: one lot with the broker's quantity and average cost, dated <paramref name="acquiredOn"/>.</summary>
    public static Holding Imported(BrokerHolding? row = null, DateOnly? acquiredOn = null, bool usesDefaultDate = false) =>
        Holding.Open(Account, Bucket, null, null, row ?? Row(), acquiredOn ?? new DateOnly(2025, 1, 1), usesDefaultDate, LotSource.Import, T0);
}
