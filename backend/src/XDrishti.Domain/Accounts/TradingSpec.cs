namespace XDrishti.Domain.Accounts;

/// <summary>The complete trading configuration of an account as the owner entered it (everything except whether the role is on).</summary>
public sealed record TradingSpec(
    TradingMode Mode,
    Guid? RiskProfileId,
    CapitalSource CapitalSource,
    decimal? CapitalAmount,
    bool Intraday,
    bool Swing,
    Instruments IntradayInstruments,
    Instruments SwingBuyInstruments,
    Instruments SwingSellInstruments,
    TradingOverrides Overrides);
