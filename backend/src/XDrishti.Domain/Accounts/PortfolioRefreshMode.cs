namespace XDrishti.Domain.Accounts;

/// <summary>When the holdings of an account refresh by themselves (ADR 0012, ADR 0013). Stored by name — never rename a value.</summary>
public enum PortfolioRefreshMode
{
    /// <summary>Never; only the owner's "Sync now" refreshes (the default).</summary>
    Off,

    /// <summary>Once a day, after a fixed India time.</summary>
    Daily,
}
