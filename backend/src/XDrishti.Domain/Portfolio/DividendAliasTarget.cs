namespace XDrishti.Domain.Portfolio;

/// <summary>What a remembered scrip name points to (ADR 0028).</summary>
public enum DividendAliasTarget
{
    /// <summary>A holding of the account.</summary>
    Holding,

    /// <summary>A share or ETF of the instrument master that is not held.</summary>
    Instrument,

    /// <summary>Nothing: dividends of that name stay unallocated.</summary>
    None,
}
