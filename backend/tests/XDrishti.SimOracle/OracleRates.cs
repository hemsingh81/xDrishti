namespace XDrishti.SimOracle;

/// <summary>The ten rates of one segment (ADR 0042 D4). Percent fields are in percent; money in rupees.</summary>
public sealed record OracleRates(
    decimal BrokeragePct,
    decimal BrokerageCap,
    decimal BrokerageMin,
    decimal SttBuyPct,
    decimal SttSellPct,
    decimal ExchangePct,
    decimal SebiPerCrore,
    decimal StampBuyPct,
    decimal OtherPct,
    decimal DpChargePerSell);
