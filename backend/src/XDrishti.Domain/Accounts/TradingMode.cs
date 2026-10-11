namespace XDrishti.Domain.Accounts;

/// <summary>How an account trades. Automated modes come in a later phase. Stored by name — never rename a value.</summary>
public enum TradingMode
{
    Paper,
    ManualLive,
}
