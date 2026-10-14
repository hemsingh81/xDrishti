namespace XDrishti.Domain.Portfolio;

/// <summary>What a ledger entry is about (ADR 0025), decided from Dhan's voucher description and narration. Stored by its snake_case code (<see cref="LedgerClasses"/>) — never rename a value.</summary>
public enum LedgerClass
{
    /// <summary>Money added to the trading account.</summary>
    FundsIn,

    /// <summary>Money paid out to the owner's bank.</summary>
    FundsOut,

    /// <summary>A dividend credited (account-level: not matched to a holding).</summary>
    Dividend,

    /// <summary>Brokerage, taxes, depository and other charges debited.</summary>
    Charges,

    /// <summary>Anything else, including the settlement of trades and everything unrecognised.</summary>
    Other,
}
